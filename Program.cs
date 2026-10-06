using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using RadarV2.Data;
using RadarV2.Services;
using RadarV2.Services.Ingestion;
using RadarV2.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Compress text payloads (JSON, HTML, CSS, JS) before they leave the process. On Railway and
// Azure this also covers the proxy hop, since TLS terminates at their edge and the internal
// request is plain HTTP — but EnableForHttps is on anyway so a direct HTTPS hit to Kestrel is
// compressed too. Brotli first: better ratio, and every browser that can reach this app
// negotiates it; Gzip stays as the fallback.
//
// text/event-stream is deliberately absent from the MIME list: buffering an SSE response in order
// to compress it would destroy chat streaming (see Helpers/SseWriter). ResponseCompression already
// skips that content type, and the default list does not include it either.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

// ── JSON API (React SPA backend) ────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Camel-case + readable enum strings instead of numeric enum values.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// JWT auth for the API. Signing key lives in Auth:Jwt (appsettings / env).
// Fallback values keep local dev working when the section is missing — but
// never in Production: that fallback key is public (it's sitting in this
// file in a public repo), so silently using it there would let anyone forge
// a valid token. Fail fast instead of starting up insecure.
const string DevOnlyJwtKey = "radar-dev-only-signing-key-change-me-0123456789abcdef";
var jwtSection = builder.Configuration.GetSection("Auth:Jwt");
var jwtIssuer   = jwtSection["Issuer"]   ?? "Radar";
var jwtAudience = jwtSection["Audience"] ?? "Radar";
var jwtKey      = jwtSection["Key"]      ?? DevOnlyJwtKey;

if (builder.Environment.IsProduction() && jwtKey == DevOnlyJwtKey)
{
    throw new InvalidOperationException(
        "Refusing to start in Production without a real Auth:Jwt:Key configured. " +
        "Set the Auth__Jwt__Key (and ideally Auth__Jwt__Issuer / Auth__Jwt__Audience) " +
        "environment variable to a unique secret — never the checked-in dev default.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        // Return clean JSON for failed API auth rather than a redirect or HTML.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Forbidden\"}");
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddMemoryCache();

// CORS for the React SPA (separate origin in dev and once deployed).
// JWT bearer auth means no cookies cross the origin, so no AllowCredentials needed.
// Allowed origins come from config (Cors:AllowedOrigins) so adding a custom domain
// later is an env var change, not a code change + redeploy.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173"];
var configuredOrigins = allowedOrigins.ToHashSet(StringComparer.OrdinalIgnoreCase);

static bool IsHostedSpaOrigin(string origin, ISet<string> configuredOrigins)
{
    if (configuredOrigins.Contains(origin)) return true;
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        return false;

    // Vercel creates a different HTTPS hostname for preview and production
    // deployments. Both need to reach the API during frontend deployments.
    return uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith(".netlify.app", StringComparison.OrdinalIgnoreCase);
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Spa", policy => policy
        .SetIsOriginAllowed(origin => IsHostedSpaOrigin(origin, configuredOrigins))
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// MongoDB
builder.Services.AddSingleton<RadarDatabase>();

// OpenRouter (LLM transformation)
builder.Services.AddHttpClient("OpenRouter", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    // Ensure a trailing slash: requests use a relative path ("chat/completions"), and without it
    // HttpClient drops the last base segment ("/v1") and hits the wrong URL.
    var baseUrl = (config["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1").TrimEnd('/') + "/";
    client.BaseAddress = new Uri(baseUrl);
    var apiKey = config["OpenRouter:ApiKey"] ?? string.Empty;
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    client.DefaultRequestHeaders.Add("HTTP-Referer", "https://radar.app");
    client.DefaultRequestHeaders.Add("X-Title", "Radar");
});

// RSS / Atom / OpenAlex feeds — long timeout, custom UA
builder.Services.AddHttpClient("Ingestion", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "Radar-Intelligence-Bot/1.0 (radar.app)");
});

// Mediastack — free tier is HTTP only, never redirect to HTTPS
builder.Services.AddHttpClient("Mediastack", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["Mediastack:BaseUrl"] ?? "http://api.mediastack.com/v1/");
    client.Timeout     = TimeSpan.FromSeconds(20);
});

// PodcastIndex — base URL only; auth headers are set per-request in PodcastIndexIngestionService
builder.Services.AddHttpClient("PodcastIndex", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["PodcastIndex:BaseUrl"] ?? "https://api.podcastindex.org/api/1.0/");
    client.Timeout     = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Add("User-Agent", "Radar/1.0 (radar.app)");
});

// Taddy GraphQL
builder.Services.AddHttpClient("Taddy", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["Taddy:BaseUrl"] ?? "https://api.taddy.org");
    client.Timeout     = TimeSpan.FromSeconds(30);
    var userId = config["Taddy:UserId"] ?? string.Empty;
    var apiKey = config["Taddy:ApiKey"] ?? string.Empty;
    client.DefaultRequestHeaders.Add("X-USER-ID", userId);
    client.DefaultRequestHeaders.Add("X-API-KEY",  apiKey);
});

// Groq Whisper transcription
builder.Services.AddHttpClient("GroqWhisper", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["GroqWhisper:BaseUrl"] ?? "https://api.groq.com/openai/v1/");
    client.Timeout     = TimeSpan.FromMinutes(5); // audio transcription can take a while
    var apiKey = config["GroqWhisper:ApiKey"] ?? string.Empty;
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
});

// OpenAlex (academic search — free, no key required)
builder.Services.AddHttpClient("OpenAlex", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("User-Agent", "RadarResearch/1.0 (radar.app; mailto:hello@radar.app)");
});
builder.Services.AddScoped<OpenAlexClient>();

// YouTube — channel pages and public Atom feeds need a browser-like User-Agent.
builder.Services.AddHttpClient("YouTube", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
});
builder.Services.AddScoped<YouTubeClient>();
builder.Services.AddScoped<YouTubeIngestionService>();

// Ingestion services
builder.Services.AddScoped<RssIngestionService>();
builder.Services.AddScoped<ContentEnricherService>();
builder.Services.AddScoped<MediastackIngestionService>();
builder.Services.AddScoped<PodcastIndexIngestionService>();
builder.Services.AddScoped<TaddyClient>();
builder.Services.AddScoped<GroqWhisperClient>();
// Registered as a singleton so the scheduled-jobs endpoint can trigger a cycle on demand, and
// hosted (same instance) so it also runs on the in-process 6h timer.
builder.Services.AddSingleton<ContentIngestionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ContentIngestionService>());

// Auth + session
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Real service implementations
builder.Services.AddScoped<IUserProfileService, MongoUserProfileService>();
builder.Services.AddScoped<IIntelligenceFeedService, MongoIntelligenceFeedService>();
builder.Services.AddScoped<IAskRadarService, OpenRouterAskRadarService>();
builder.Services.AddSingleton<IAiEngine, OpenRouterAiEngine>();
builder.Services.AddScoped<ILibraryService, MongoLibraryService>();

// Remaining stub services (replace as backend expands)
builder.Services.AddScoped<INavigatorService, MongoNavigatorService>();
builder.Services.AddScoped<IOpportunityService, MongoOpportunityService>();
builder.Services.AddScoped<IResearchService, MongoResearchService>();
builder.Services.AddScoped<IRoadmapService, MongoRoadmapService>();
builder.Services.AddScoped<IWeeklyBriefService, MongoWeeklyBriefService>();
builder.Services.AddScoped<IProjectStudioService, MongoProjectStudioService>();
builder.Services.AddScoped<INotebookService, MongoNotebookService>();
builder.Services.AddScoped<IClipsService, MongoClipsService>();
builder.Services.AddScoped<ICaptureService, MongoCaptureService>();
builder.Services.AddScoped<ICompareService, MongoCompareService>();
builder.Services.AddScoped<ISourceService, MongoSourceService>();
builder.Services.AddScoped<ITopicService, MongoTopicService>();
builder.Services.AddScoped<IPlansService, MongoPlansService>();
builder.Services.AddScoped<ILearningMentorService, MongoLearningMentorService>();
builder.Services.AddScoped<IAnalyticsService, MongoAnalyticsService>();
builder.Services.AddScoped<IPersonalizedWhyService, PersonalizedWhyService>();
builder.Services.AddScoped<IContentReportService, MongoContentReportService>();
builder.Services.AddScoped<IBehavioralSignalService, BehavioralSignalService>();
builder.Services.AddScoped<AiRelevanceService>();
builder.Services.AddScoped<AiOpportunityMatchService>();

var app = builder.Build();

// Railway (and most PaaS hosts) terminate TLS at their edge and forward plain
// HTTP internally with X-Forwarded-Proto: https. Without this, UseHttpsRedirection
// below never sees the request as HTTPS and redirects every single request,
// forever (the client already used HTTPS, so it just loops). Must run first.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Must sit before anything that writes a response body (static assets, controllers, Blazor).
app.UseResponseCompression();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseCors("Spa");

// API auth: authenticate the JWT, then seed the scoped user session from the
// token claims so the existing services (IUserProfileService etc.) resolve the
// current user for the request.
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var session = context.RequestServices.GetRequiredService<IUserSessionService>();
    if (context.User.Identity?.IsAuthenticated == true && !session.IsAuthenticated)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var userName = context.User.FindFirstValue(ClaimTypes.Name)
                       ?? context.User.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
                       ?? string.Empty;
        if (userId is not null)
            session.SetUser(userId, userName);
    }
    await next(context);
});
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

// The Razor UI has been retired: this host is API-only and the React SPA is
// deployed separately. Any response that carries an error status but no body
// (unmatched route, auth failure that didn't already write JSON, etc.) gets a
// JSON envelope instead of the HTML page a browser would expect.
app.UseStatusCodePages(async statusCodeContext =>
{
    var response = statusCodeContext.HttpContext.Response;
    if (response.HasStarted) return;

    response.ContentType = "application/json";
    var message = response.StatusCode switch
    {
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden    => "Forbidden",
        StatusCodes.Status404NotFound     => "Not Found",
        _                                 => "Error"
    };
    await response.WriteAsync($"{{\"error\":\"{message}\"}}");
});

app.Run();
