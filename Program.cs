using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using RadarV2.Components;
using RadarV2.Data;
using RadarV2.Services;
using RadarV2.Services.Ingestion;
using RadarV2.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

        // Return clean JSON for failed API auth instead of letting the
        // Blazor /not-found status page re-execute swallow the response.
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

// CORS for the React SPA (separate origin in dev and once deployed).
// JWT bearer auth means no cookies cross the origin, so no AllowCredentials needed.
// Allowed origins come from config (Cors:AllowedOrigins) so adding a custom domain
// later is an env var change, not a code change + redeploy.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Spa", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// MongoDB
builder.Services.AddSingleton<RadarDatabase>();

// OpenRouter (LLM transformation)
builder.Services.AddHttpClient("OpenRouter", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1");
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

// Ingestion services
builder.Services.AddScoped<RssIngestionService>();
builder.Services.AddScoped<ContentEnricherService>();
builder.Services.AddScoped<MediastackIngestionService>();
builder.Services.AddScoped<PodcastIndexIngestionService>();
builder.Services.AddScoped<TaddyClient>();
builder.Services.AddScoped<GroqWhisperClient>();
builder.Services.AddHostedService<ContentIngestionService>();

// Auth + session
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Real service implementations
builder.Services.AddScoped<IUserProfileService, MongoUserProfileService>();
builder.Services.AddScoped<IIntelligenceFeedService, MongoIntelligenceFeedService>();
builder.Services.AddScoped<IAskRadarService, OpenRouterAskRadarService>();
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

var app = builder.Build();

// Railway (and most PaaS hosts) terminate TLS at their edge and forward plain
// HTTP internally with X-Forwarded-Proto: https. Without this, UseHttpsRedirection
// below never sees the request as HTTPS and redirects every single request,
// forever (the client already used HTTPS, so it just loops). Must run first.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseCors("Spa");

// API auth: authenticate the JWT, then seed the scoped user session from the
// token claims so the existing Blazor-era services (IUserProfileService etc.)
// resolve the current user exactly as they did per-circuit.
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
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Unmatched /api routes get a JSON 404 (the Blazor status-page re-execute above
// would otherwise return an HTML page to API clients).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && context.Response.StatusCode == StatusCodes.Status404NotFound
        && !context.Response.HasStarted)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Not Found\"}");
    }
    else
    {
        await next(context);
    }
});

app.Run();
