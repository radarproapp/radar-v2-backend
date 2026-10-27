using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Central processor and single boundary for all Radar AI activities.
/// Orchestrates cloud LLMs (OpenRouter) with an integrated resilient deterministic intelligence brain
/// to ensure every activity (feed personalization, learning paths, learn hub, opportunities,
/// academic synthesis, mentor tutoring, and roadmaps) is reliably processed and audited.
/// </summary>
public sealed class OpenRouterAiEngine : IAiEngine
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly RadarDatabase _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenRouterAiEngine> _log;

    /// <summary>Prompts currently being generated in the background, so a burst of requests for
    /// the same page triggers one provider call rather than one per request.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _inFlight = new();

    public OpenRouterAiEngine(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        IMemoryCache cache,
        ILogger<OpenRouterAiEngine> log)
    {
        _httpFactory = httpFactory;
        _config = config;
        _db = db;
        _cache = cache;
        _log = log;
    }

    /// <summary>
    /// Features that write structured content, rather than scoring or labelling something.
    /// </summary>
    /// <remarks>
    /// The two want opposite things from a timeout. Scoring a feed item happens while somebody
    /// waits for a page, so 20 seconds is already generous. Writing a section of a learning path
    /// is a deliberate action behind a progress indicator, and 20 seconds is not enough — the
    /// model was being cut off mid-answer every time, which is why every section came back with
    /// no lessons even once the API key was valid.
    /// </remarks>
    private static bool IsGeneration(string feature) =>
        feature is "learning-section" or "learning-pathway" or "document-subject";

    /// <summary>How long a single attempt may take.</summary>
    private TimeSpan AttemptBudget(string feature) => TimeSpan.FromSeconds(
        IsGeneration(feature)
            ? _config.GetValue("OpenRouter:GenerationTimeoutSeconds", 55)
            : _config.GetValue("OpenRouter:TimeoutSeconds", 20));

    /// <summary>
    /// How many times to try. Generation gets one: a retry after a 55-second timeout doubles the
    /// wait for an answer that is unlikely to arrive any faster the second time.
    /// </summary>
    private static int MaxAttempts(string feature) => IsGeneration(feature) ? 1 : 2;

    /// <summary>
    /// How many tokens one answer may use.
    ///
    /// Not an economy measure — a correctness one. OpenRouter prices the *reservation*, not the
    /// answer: with no max_tokens it reserves the model's full 16,384-token allowance and refuses
    /// the request outright when the key cannot cover that much, even though the reply would have
    /// cost a fraction of it. Measured on staging: HTTP 402, "you requested up to 16384 tokens,
    /// but can only afford 15174", on a key with credit left and USD 2.45 still unspent against
    /// its own monthly cap. Every feature in the product was falling back to the deterministic
    /// brain, and the engine reported the reason to nobody but /api/ai/status.
    ///
    /// A section of a learning path is long. An explanation of a paper, a why-paragraph or a
    /// quiz is not, and reserving sixteen thousand tokens for a four-hundred-token answer is how
    /// a key with headroom stops working.
    /// </summary>
    private int MaxOutputTokens(string feature) =>
        IsGeneration(feature)
            ? _config.GetValue("OpenRouter:GenerationMaxTokens", 8000)
            : _config.GetValue("OpenRouter:MaxTokens", 1600);

    /// <summary>
    /// How long to wait before trying a rejected request again.
    ///
    /// Most non-success statuses are terminal for the call: a rejected key or a malformed request
    /// will be rejected identically a quarter of a second later, and making the reader wait for
    /// that helps nobody. Two are not terminal, and both were being treated as though they were.
    ///
    /// 402 "in_flight_budget_exhausted" means the requests already in flight have reserved the
    /// available credit — "retry after in-flight requests settle, or add credits". The enricher
    /// runs four at a time, so they exhaust it against each other.
    ///
    /// 429 is the upstream provider rate-limiting us, which deepseek does under load. Measured on
    /// staging: a research-gaps call losing to one, in the same window as calls that succeeded.
    ///
    /// Both need seconds, because what has to happen is somebody else's request finishing. A
    /// 250 ms backoff only spends the retry.
    /// </summary>
    private static TimeSpan RetryDelayFor(int statusCode, string? errorBody, int attempt)
    {
        var slow = statusCode == 429
                   || (errorBody is not null
                       && (errorBody.Contains("in_flight_budget_exhausted", StringComparison.OrdinalIgnoreCase)
                           || errorBody.Contains("in-flight requests settle", StringComparison.OrdinalIgnoreCase)
                           || errorBody.Contains("rate limit", StringComparison.OrdinalIgnoreCase)));

        return slow
            ? TimeSpan.FromSeconds(1.5 * attempt)
            : TimeSpan.FromMilliseconds(250 * attempt);
    }

    public async Task<T?> GenerateJsonAsync<T>(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        // Applied at the boundary rather than at each call site: every feature funnels through
        // here, so the plain-English rule cannot drift between them or be forgotten on a new one.
        systemPrompt = PlainEnglish.Apply(systemPrompt);

        var sw = Stopwatch.StartNew();
        var cacheKey = CacheKey(feature, systemPrompt, userPrompt);
        if (_cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            await RecordActivityAsync(feature, null, "Cached", true, sw.ElapsedMilliseconds, "Served from memory cache", ct);
            return cached;
        }

        var apiKey = _config["OpenRouter:ApiKey"];
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var withinBudget = await WithinBudgetAsync(feature, ct);

        // Why the model was not used. Null means it was.
        var failureReason = !hasApiKey ? "No OpenRouter:ApiKey configured."
            : !withinBudget ? "Monthly spend cap reached."
            : null;

        if (hasApiKey && withinBudget)
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                response_format = new { type = "json_object" },
                temperature = 0.25,
                max_tokens = MaxOutputTokens(feature),
            });

            // Overall deadline across attempts. The per-request HttpClient timeout bounds one call;
            // without this a retry could stack a second full timeout on top of the first, which is how
            // a single slow provider turned into a 40-second page.
            var deadline = TimeSpan.FromSeconds(
                IsGeneration(feature)
                    ? _config.GetValue("OpenRouter:GenerationDeadlineSeconds", 60)
                    : _config.GetValue("OpenRouter:DeadlineSeconds", 25));

            var maxAttempts = MaxAttempts(feature);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    };
                    using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    attemptCts.CancelAfter(AttemptBudget(feature));

                    using var response = await _httpFactory
                        .CreateClient("OpenRouter")
                        .SendAsync(request, attemptCts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        // Kept, not just logged. The log line lives in the host's dashboard; this
                        // reason reaches /api/ai/status, where it can be read without one.
                        var detail = await response.Content.ReadAsStringAsync(ct);
                        failureReason = $"HTTP {(int)response.StatusCode} {response.StatusCode}: {Trim(detail)}";
                        _log.LogWarning("OpenRouter feature {Feature} returned {Status} on attempt {Attempt}: {Detail}", feature, response.StatusCode, attempt, detail);
                        var backoff = RetryDelayFor((int)response.StatusCode, detail, attempt);
                        if (attempt == maxAttempts || sw.Elapsed + backoff >= deadline) break;
                        await Task.Delay(backoff, ct);
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var envelope = JsonDocument.Parse(json);
                    var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var stripped = StripFence(content);
                        var value = JsonSerializer.Deserialize<T>(stripped, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (value is not null)
                        {
                            _cache.Set(cacheKey, value, TimeSpan.FromHours(_config.GetValue("OpenRouter:CacheHours", 12)));
                            await RecordUsageAsync(feature, envelope.RootElement, ct);
                            await RecordActivityAsync(feature, null, "LLM", true, sw.ElapsedMilliseconds, $"Processed via {_config["OpenRouter:Model"] ?? "deepseek/deepseek-chat"}", ct);
                            return value;
                        }
                    }
                }
                catch (Exception ex) when (attempt < maxAttempts && sw.Elapsed < deadline && ex is not OperationCanceledException)
                {
                    failureReason = $"{ex.GetType().Name}: {ex.Message}";
                    _log.LogWarning(ex, "OpenRouter feature {Feature} failed on attempt {Attempt}; retrying", feature, attempt);
                    await Task.Delay(250, ct);
                }
                catch (Exception ex)
                {
                    failureReason = $"{ex.GetType().Name}: {ex.Message}";
                    _log.LogWarning(ex, "OpenRouter feature {Feature} failed; engaging resilient brain", feature);
                    break;
                }
            }
        }

        // Resilient Deterministic Intelligence Brain
        //
        // Reaching here is a failure even when the fallback returns something. It was recorded as
        // success=true with the summary "Processed via resilient deterministic intelligence
        // brain", so /api/ai/status reported a healthy engine while every learning section came
        // back empty. The reason now travels with the record, because the log line that holds it
        // lives in the host's dashboard and this does not.
        var fallback = GetDeterministicJsonFallback<T>(feature, systemPrompt, userPrompt);
        var summary = failureReason is null
            ? "Processed via resilient deterministic intelligence brain"
            : $"Fell back to the deterministic brain. Cloud call failed: {failureReason}";

        await RecordActivityAsync(feature, null, "DeterministicBrain", false, sw.ElapsedMilliseconds, summary, ct);
        if (fallback is not null)
        {
            _cache.Set(cacheKey, fallback, TimeSpan.FromMinutes(30));
        }
        return fallback;
    }

    public async Task<string?> GenerateTextAsync(string feature, string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        // Applied at the boundary rather than at each call site: every feature funnels through
        // here, so the plain-English rule cannot drift between them or be forgotten on a new one.
        systemPrompt = PlainEnglish.Apply(systemPrompt);

        var sw = Stopwatch.StartNew();
        var apiKey = _config["OpenRouter:ApiKey"];
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var withinBudget = await WithinBudgetAsync(feature, ct);

        var failureReason = !hasApiKey ? "No OpenRouter:ApiKey configured."
            : !withinBudget ? "Monthly spend cap reached."
            : null;

        if (hasApiKey && withinBudget)
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.25,
                max_tokens = MaxOutputTokens(feature),
            });

            // The same loop shape as the json path, and for the same reason.
            //
            // This had none: one attempt, and a non-success status logged and swallowed. So an
            // in-flight budget refusal — which resolves on its own once a sibling request
            // finishes — ended the call outright.
            var deadline = TimeSpan.FromSeconds(
                IsGeneration(feature)
                    ? _config.GetValue("OpenRouter:GenerationDeadlineSeconds", 60)
                    : _config.GetValue("OpenRouter:DeadlineSeconds", 25));

            var maxAttempts = MaxAttempts(feature);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    };
                    using var response = await _httpFactory.CreateClient("OpenRouter").SendAsync(request, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(ct);
                        using var envelope = JsonDocument.Parse(json);
                        var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            await RecordUsageAsync(feature, envelope.RootElement, ct);
                            await RecordActivityAsync(feature, null, "LLM", true, sw.ElapsedMilliseconds, "Text generated via OpenRouter", ct);
                            return content;
                        }

                        failureReason = "HTTP 200 with no message content.";
                        _log.LogWarning(
                            "OpenRouter returned 200 for {Feature} with no message content; using the deterministic fallback.",
                            feature);
                        break;
                    }

                    // Kept rather than only logged. A rejected key, an exhausted balance and a
                    // rate limit all arrive as a status rather than an exception, so the catch
                    // below never saw them — every call could be failing and the only symptom was
                    // prose that read slightly generically.
                    var error = await response.Content.ReadAsStringAsync(ct);
                    failureReason = $"HTTP {(int)response.StatusCode} {response.StatusCode}: {Trim(error)}";
                    _log.LogError(
                        "OpenRouter rejected {Feature}: HTTP {Status} on attempt {Attempt}. {Body}",
                        feature,
                        (int)response.StatusCode,
                        attempt,
                        error.Length > 400 ? error[..400] : error);

                    var backoff = RetryDelayFor((int)response.StatusCode, error, attempt);
                    if (attempt == maxAttempts || sw.Elapsed + backoff >= deadline) break;
                    await Task.Delay(backoff, ct);
                }
                catch (Exception ex) when (attempt < maxAttempts && sw.Elapsed < deadline && ex is not OperationCanceledException)
                {
                    failureReason = $"{ex.GetType().Name}: {ex.Message}";
                    _log.LogWarning(ex, "OpenRouter text feature {Feature} failed on attempt {Attempt}; retrying", feature, attempt);
                    await Task.Delay(250, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failureReason = $"{ex.GetType().Name}: {ex.Message}";
                    _log.LogWarning(ex, "OpenRouter text feature {Feature} failed; engaging resilient brain", feature);
                    break;
                }
            }
        }

        var fallback = GetDeterministicTextFallback(feature, systemPrompt, userPrompt);

        // The reason travels with the record, as it does on the json path. Without it a text
        // failure was invisible on /api/ai/status: the summary read "Text generated via resilient
        // brain" whether the key was revoked, the balance was out, or nothing had gone wrong.
        var summary = failureReason is null
            ? "Text generated via resilient brain"
            : $"Fell back to the deterministic brain. Cloud call failed: {failureReason}";

        await RecordActivityAsync(feature, null, "DeterministicBrain", false, sw.ElapsedMilliseconds, summary, ct);
        return fallback;
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string feature,
        string systemPrompt,
        IReadOnlyCollection<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        systemPrompt = PlainEnglish.Apply(systemPrompt);

        var sw = Stopwatch.StartNew();
        var apiKey = _config["OpenRouter:ApiKey"];
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var withinBudget = await WithinBudgetAsync(feature, ct);

        var streamedChunks = 0;
        if (hasApiKey && withinBudget)
        {
            var promptMessages = new List<object> { new { role = "system", content = systemPrompt } };
            promptMessages.AddRange(messages.TakeLast(20).Select(m => new { role = m.Role, content = m.Content }));
            var body = JsonSerializer.Serialize(new
            {
                model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
                messages = promptMessages,
                stream = true,
                // The third call site, and it was missed when the other two were capped. A chat
                // turn reserving the model's full allowance is refused on a key near its limit
                // for the same reason the other two were.
                max_tokens = MaxOutputTokens(feature),
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpFactory.CreateClient("OpenRouter").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "OpenRouter streaming feature {Feature} failed; engaging resilient stream", feature);
                response = null;
            }

            if (response is not null)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);
                while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
                {
                    if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                    var data = line[6..];
                    if (data == "[DONE]") break;
                    string? chunk = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        chunk = doc.RootElement.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString();
                    }
                    catch (JsonException) { }
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        streamedChunks++;
                        yield return chunk;
                    }
                }

                if (streamedChunks > 0)
                {
                    await RecordActivityAsync(feature, null, "LLM", true, sw.ElapsedMilliseconds, $"Streamed {streamedChunks} chunks via OpenRouter", ct);
                    yield break;
                }
            }
        }

        // Resilient Socratic Tutor Chat Stream
        var fallbackChunks = GetDeterministicChatStream(feature, systemPrompt, messages);
        foreach (var chunk in fallbackChunks)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return chunk;
        }
        await RecordActivityAsync(feature, null, "DeterministicBrain", true, sw.ElapsedMilliseconds, "Streamed via resilient Socratic tutor stream", ct);
    }

    public Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        return Task.FromResult<float[]?>(null);
    }

    /// <summary>One line of a provider error body — enough to act on, short enough to store.</summary>
    private static string Trim(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return "(no body)";
        var flat = detail.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 300 ? flat : flat[..300] + "…";
    }

    public async Task RecordActivityAsync(
        string feature,
        string? userId,
        string mode,
        bool success,
        long durationMs,
        string? summary = null,
        CancellationToken ct = default)
    {
        try
        {
            var logDoc = new AiActivityLogDoc
            {
                Feature = feature,
                UserId = userId,
                Mode = mode,
                Success = success,
                DurationMs = durationMs,
                Timestamp = DateTime.UtcNow,
                Summary = summary
            };

            await _db.AiActivities.InsertOneAsync(logDoc, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Non-fatal: failed to record AI activity log for {Feature}", feature);
        }
    }

    public async Task<AiEngineStats> GetEngineStatsAsync(CancellationToken ct = default)
    {
        var cap = _config.GetValue<decimal>("OpenRouter:MonthlySpendCapUsd", 25m);
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var quota = await _db.IngestionQuotas.Find(q => q.Id == $"openrouter:{month}").FirstOrDefaultAsync(ct);
        var apiKey = _config["OpenRouter:ApiKey"];

        var recentActivities = await _db.AiActivities
            .Find(_ => true)
            .SortByDescending(a => a.Timestamp)
            .Limit(20)
            .ToListAsync(ct);

        var totalActivities = await _db.AiActivities.CountDocumentsAsync(_ => true, cancellationToken: ct);

        // Whether the *most recent* attempt to reach the provider failed — not merely whether a
        // failure appears anywhere in the recent list. The first version reported degraded on the
        // strength of any failure in the last twenty activities, so the engine still read as down
        // after it had started working again. Read from the activity log rather than held in
        // memory, so it survives a restart and is the same answer on every instance.
        var lastCloudAttempt = recentActivities
            .FirstOrDefault(a => a.Mode == "LLM"
                || (a.Summary is not null && a.Summary.Contains("Cloud call failed", StringComparison.Ordinal)));

        var lastCloudFailure = lastCloudAttempt is not null && lastCloudAttempt.Mode != "LLM"
            ? lastCloudAttempt.Summary
            : null;

        // Group counts by feature
        var featureCounts = new Dictionary<string, long>();
        try
        {
            var pipeline = await _db.AiActivities
                .Aggregate()
                .Group(a => a.Feature, g => new { Feature = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            foreach (var item in pipeline)
            {
                if (!string.IsNullOrEmpty(item.Feature))
                    featureCounts[item.Feature] = item.Count;
            }
        }
        catch { }

        return new AiEngineStats
        {
            EngineName = "Radar Central AI Engine",
            Model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
            // Whether the model is answering, not whether a key is present.
            //
            // This read "configured and within budget", so a revoked key reported a live engine
            // for as long as the string was set. Staging ran that way: isCloudLlmActive true,
            // budget fine, every call failing 401 "User not found", every learning section coming
            // back empty, and nothing on this endpoint saying so.
            IsCloudLlmActive = !string.IsNullOrWhiteSpace(apiKey)
                && (quota?.SpendUsd ?? 0m) < cap
                && lastCloudFailure is null,
            IsBudgetExhausted = !string.IsNullOrWhiteSpace(apiKey) && (quota?.SpendUsd ?? 0m) >= cap,
            DegradedReason =
                string.IsNullOrWhiteSpace(apiKey) ? "No OpenRouter API key is configured."
                : lastCloudFailure is not null ? lastCloudFailure
                : (quota?.SpendUsd ?? 0m) >= cap
                    ? $"Monthly spend cap of ${cap} reached (${quota?.SpendUsd ?? 0m:0.00} used). Radar is running on the deterministic brain until next month or until the cap is raised."
                    : null,
            MonthlySpendUsd = quota?.SpendUsd ?? 0m,
            MonthlySpendCapUsd = cap,
            TotalActivitiesProcessed = totalActivities,
            ActivityCounts = featureCounts,
            RecentActivities = recentActivities
        };
    }

    private static string CacheKey(string feature, string systemPrompt, string userPrompt) =>
        $"radar:ai:{feature}:{Hash(systemPrompt + "\n" + userPrompt)}";

    public bool TryGetCachedJson<T>(string feature, string systemPrompt, string userPrompt, out T? value)
    {
        if (_cache.TryGetValue(CacheKey(feature, systemPrompt, userPrompt), out T? cached) && cached is not null)
        {
            value = cached;
            return true;
        }

        value = default;
        return false;
    }

    public void WarmJson<T>(string feature, string systemPrompt, string userPrompt)
    {
        var key = CacheKey(feature, systemPrompt, userPrompt);
        if (_cache.TryGetValue(key, out T? existing) && existing is not null) return;
        if (!_inFlight.TryAdd(key, 0)) return;

        // Detached on purpose: nothing awaits this and the caller has already returned a response.
        // Every dependency of this class is a singleton, so there is no scoped service to outlive.
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await GenerateJsonAsync<T>(feature, systemPrompt, userPrompt, cts.Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Background warm for {Feature} failed", feature);
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        });
    }

    // ── Resilient Deterministic Intelligence Brain ─────────────────────────────

    private T? GetDeterministicJsonFallback<T>(string feature, string systemPrompt, string userPrompt)
    {
        try
        {
            var json = SynthesizeFeatureJson(feature, userPrompt);
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to deserialize deterministic fallback for {Feature}", feature);
            return default;
        }
    }

    private string SynthesizeFeatureJson(string feature, string prompt)
    {
        var lowerPrompt = prompt.ToLowerInvariant();

        // Personalised why/edge/next-move is deliberately absent here.
        //
        // Returning anything for it is actively harmful: PersonalizedWhyService treats a non-empty
        // "why" as a real answer, persists it with IsGenerated = true, and FeedController then
        // prefers it over InterestPersonalization.Explain() — which produces a grounded sentence
        // naming this user's own goal, capability and location for this specific item.
        //
        // So a canned string here does not fill a gap, it shadows the better answer and caches
        // itself in its place. Falling through to "{}" leaves Why empty, GenerateAsync returns
        // null, nothing is persisted, and the grounded explanation is used instead.

        // 2. Opportunity Matching
        if (feature.Contains("opportunity"))
        {
            var matches = new List<string>();
            var idMatches = Regex.Matches(prompt, "\"id\":\"([^\"]+)\"");
            foreach (Match m in idMatches)
            {
                var id = m.Groups[1].Value;
                matches.Add($$"""{"id":"{{id}}","score":0.88,"confidence":0.92,"whyItFits":"Strong alignment with your active roadmap and target career goals.","preparationSteps":["Review technical requirements and prerequisites","Align your portfolio projects to highlighted competencies","Prepare a tailored application highlighting your relevant roadmap milestones"]}""");
            }

            if (matches.Count == 0)
            {
                matches.Add("""{"id":"default","score":0.85,"confidence":0.9,"whyItFits":"High contextual relevance to your professional profile.","preparationSteps":["Review prerequisites","Prepare submission"]}""");
            }

            return $$"""{ "matches": [{{string.Join(",", matches)}}] }""";
        }

        // 3. Mentor Quiz Generation
        if (feature.Contains("quiz") || feature.Contains("mentor-generation"))
        {
            var topic = ExtractTopic(prompt) ?? "this topic";
            return $$"""
            {
              "nextMove": "Your next move: Practice applying the concepts from this quiz in a real code or architecture scenario.",
              "detectedWeakAreas": ["{{topic}} Implementation Edge Cases", "{{topic}} Production Debugging"],
              "questions": [
                {
                  "question": "What is the primary architectural principle governing {{topic}}?",
                  "options": [
                    "Separation of concerns and deterministic execution",
                    "Monolithic tight coupling across modules",
                    "Unchecked shared mutable state",
                    "Exclusively synchronous blocking operations"
                  ],
                  "correctIndex": 0,
                  "explanation": "{{topic}} relies fundamentally on separation of concerns and deterministic execution to maintain consistency and scalability.",
                  "questionType": "MCQ",
                  "cognitiveLevel": "Recall",
                  "codeSnippet": null,
                  "scenarioContext": "Foundational architectural evaluation",
                  "subtopic": "{{topic}} Core Principles"
                },
                {
                  "question": "When debugging performance bottlenecks in {{topic}}, which metric provides the most accurate ground truth?",
                  "options": [
                    "Surface-level CPU percentage alone",
                    "Latency percentile distribution (p95/p99) and I/O wait times",
                    "Total lines of code executed",
                    "Memory heap allocation count without GC duration"
                  ],
                  "correctIndex": 1,
                  "explanation": "High-percentile latency distributions combined with I/O wait times reflect the actual user experience and system constraints.",
                  "questionType": "MCQ",
                  "cognitiveLevel": "Understanding",
                  "codeSnippet": null,
                  "scenarioContext": "System observability and diagnosis",
                  "subtopic": "{{topic}} Diagnostics"
                },
                {
                  "question": "A production service implementing {{topic}} experiences sudden unhandled exceptions during high concurrency. What is the most effective initial remediation?",
                  "options": [
                    "Restart the cluster repeatedly without examining logs",
                    "Introduce backpressure limits, rate limiting, and review concurrency locks",
                    "Disable all input validation to maximize throughput",
                    "Increase thread count without bound"
                  ],
                  "correctIndex": 1,
                  "explanation": "Applying backpressure controls and auditing concurrency boundaries stabilizes throughput and prevents cascading failures.",
                  "questionType": "Scenario",
                  "cognitiveLevel": "Application",
                  "codeSnippet": null,
                  "scenarioContext": "High-throughput production outage mitigation",
                  "subtopic": "{{topic}} Concurrency & Resilience"
                }
              ]
            }
            """;
        }

        // 4. Dynamic Study Plan
        if (feature.Contains("study-plan"))
        {
            var topic = ExtractTopic(prompt) ?? "this topic";
            return $$"""
            {
              "weeks": [
                {
                  "weekNumber": 1,
                  "title": "Foundations & Core Mechanics of {{topic}}",
                  "focus": "Master core syntax, data models, and fundamental execution rules.",
                  "hoursNeeded": 6,
                  "tasks": [
                    { "title": "Review fundamental principles and official documentation", "isCompleted": false },
                    { "title": "Configure local environment and run 3 baseline exercises", "isCompleted": false },
                    { "title": "Complete Mentor Recall Quiz with 80%+ score", "isCompleted": false }
                  ],
                  "milestoneReview": "Demonstrate a working local sandbox executing basic commands without errors."
                },
                {
                  "weekNumber": 2,
                  "title": "Intermediate Patterns & State Management in {{topic}}",
                  "focus": "Transition from syntax to structured problem solving and workflow integration.",
                  "hoursNeeded": 7,
                  "tasks": [
                    { "title": "Implement intermediate workflows and handle common edge cases", "isCompleted": false },
                    { "title": "Build a mini-utility solving a realistic business requirement", "isCompleted": false },
                    { "title": "Submit code to Mentor Work Review for automated feedback", "isCompleted": false }
                  ],
                  "milestoneReview": "Working mini-utility passing all validation criteria with clean modular structure."
                },
                {
                  "weekNumber": 3,
                  "title": "Production Hardening, Resilience & Real-World Application",
                  "focus": "Security, performance optimization, and portfolio artifact integration.",
                  "hoursNeeded": 8,
                  "tasks": [
                    { "title": "Conduct stress tests and audit bottlenecks", "isCompleted": false },
                    { "title": "Document architecture decisions in a technical brief", "isCompleted": false },
                    { "title": "Complete Mentor Challenge Mode diagnostic assessment", "isCompleted": false }
                  ],
                  "milestoneReview": "Complete, production-ready portfolio project with documentation."
                }
              ]
            }
            """;
        }

        // Work review is deliberately absent.
        //
        // A canned review is worse here than anywhere else in the product. Verified: a banana
        // bread recipe and a security incident report came back with byte-identical strengths,
        // weaknesses, main issue and a score of 74 — because none of it was read. Someone acting
        // on that feedback is being misled about work they are trying to improve, and a fabricated
        // score is a judgement Radar has not actually made.
        //
        // Falling through to "{}" lets the caller detect that no review happened and say so.

        // The academic paper breakdown is deliberately absent, for the same reason as the work
        // review above.
        //
        // What this used to return was a complete seven-part breakdown of a paper nobody had
        // read: "a statistically significant 31.4% improvement in operational efficacy", "robust
        // error attenuation across stress scenarios", "published in verified peer-reviewed press
        // with verifiable DOI". It was attached to whichever paper the reader had opened — so a
        // real title and a real, resolving DOI carried invented findings, which is worse than an
        // invented paper, because everything checkable about it checks out.
        //
        // Falling through to "{}" leaves the breakdown empty, and the card omits the sections it
        // has nothing for.

        // Default generic JSON
        return "{}";
    }

    private string GetDeterministicTextFallback(string feature, string systemPrompt, string userPrompt)
    {
        var topic = ExtractTopic(userPrompt);

        // No invented explanation. What this used to return - "X is a structured methodology
        // designed to solve complex operational challenges by breaking them into manageable,
        // measurable components" - was true of nothing in particular and was shown to the reader
        // in the same register as a real answer, so there was no way to tell them apart.
        // Gaps asks a different question from explain, and saying "could not write a
        // plain-English summary" to someone who asked what a paper leaves open reads as a
        // different request having been answered.
        if (feature.Contains("gap"))
        {
            return "Radar could not work out what this paper leaves open right now. Nothing here is a stand-in for it.";
        }

        if (feature.Contains("research") || feature.Contains("explain"))
        {
            return topic is null
                ? "Radar could not write a plain-English summary of this one right now. The original is linked above."
                : $"Radar could not write a plain-English summary of {topic} right now. The original is linked above.";
        }

        return "Radar could not generate this right now. Nothing here is a stand-in for it.";
    }

    private IEnumerable<string> GetDeterministicChatStream(string feature, string systemPrompt, IReadOnlyCollection<ChatMessage> messages)
    {
        var lastMsg = messages.LastOrDefault()?.Content ?? "learning topic";
        var topic = ExtractTopic(lastMsg) ?? "this topic";

        var intro = $"Let's break down **{topic}** from first principles.\n\n";
        var explanation = $"### 1. The Core Concept\n" +
                          $"At its foundation, **{topic}** is about understanding how components interact under realistic constraints. Rather than memorizing abstract rules, think of it as a set of deterministic principles that guide predictable outcomes.\n\n" +
                          $"### 2. A Real-World Example\n" +
                          $"Consider how an organization or production system handles unexpected volume or edge cases. By applying the right design pattern in **{topic}**, you isolate failures, ensure state integrity, and maintain high availability.\n\n" +
                          $"### 3. Check for Understanding\n" +
                          $"To test your mental model: *If you had to explain the single most critical constraint in {topic} to a junior colleague, which component would you highlight first and why?*\n\n" +
                          $"### Your next move: Spend 10 minutes writing down your answer to this question, then test a simple implementation in your local lab.\n";

        yield return intro;
        yield return explanation;
    }

    /// <summary>
    /// The subject a prompt is about, or null when it cannot be told.
    ///
    /// The previous version had no word boundary on its alternatives, so the "on" in "London
    /// WC2E 7PN" matched and the extracted topic was the postcode - which the deterministic
    /// fallback then described to the reader as "a structured methodology designed to solve
    /// complex operational challenges". Its other branch returned the prompt's first word, which
    /// is how a paper came back explained as the concept "Explain".
    ///
    /// Returns null rather than a guess. A caller that cannot name the subject should say less,
    /// not say something confident about a postcode.
    /// </summary>
    private static string? ExtractTopic(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = Regex.Match(
            text,
            @"\b(?:topic|learn about|teach me about|about)\s+['""]?([A-Za-z0-9\s#\+\-\.]{3,40}?)['""]?(?:[\.\n\r]|$)",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

        if (!match.Success) return null;

        var topic = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(topic) ? null : topic;
    }

    private async Task<bool> WithinBudgetAsync(string feature, CancellationToken ct)
    {
        var cap = _config.GetValue<decimal>("OpenRouter:MonthlySpendCapUsd", 25m);
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var quota = await _db.IngestionQuotas.Find(q => q.Id == $"openrouter:{month}").FirstOrDefaultAsync(ct);
        if (quota is null || quota.SpendUsd < cap) return true;

        // Previously this returned false silently, so the whole product quietly dropped to template
        // text mid-month with nothing in the logs and a green dashboard. Say it out loud.
        _log.LogWarning(
            "OpenRouter monthly spend cap reached for {Feature}: {Spend:0.00} of {Cap:0.00} USD used in {Month}. "
            + "Falling back to the deterministic brain until the cap is raised or the month rolls over.",
            feature, quota.SpendUsd, cap, month);
        return false;
    }

    private async Task RecordUsageAsync(string feature, JsonElement envelope, CancellationToken ct)
    {
        if (!envelope.TryGetProperty("usage", out var usage)) return;
        var input = usage.TryGetProperty("prompt_tokens", out var i) ? i.GetInt64() : 0;
        var output = usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt64() : 0;
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var spend = input / 1_000_000m * 0.27m + output / 1_000_000m * 1.10m;
        await _db.IngestionQuotas.UpdateOneAsync(
            q => q.Id == $"openrouter:{month}",
            Builders<IngestionQuotaDoc>.Update
                .SetOnInsert(q => q.Id, $"openrouter:{month}")
                .SetOnInsert(q => q.Service, "openrouter")
                .SetOnInsert(q => q.Month, month)
                .Inc(q => q.RequestCount, 1)
                .Inc(q => q.TotalInputTokens, input)
                .Inc(q => q.TotalOutputTokens, output)
                .Inc(q => q.SpendUsd, spend)
                .Set(q => q.UpdatedAt, DateTime.UtcNow),
            new UpdateOptions { IsUpsert = true },
            ct);
    }

    private static string StripFence(string value)
    {
        var result = value.Trim();
        if (!result.StartsWith("```")) return result;
        result = result[(result.IndexOf('\n') + 1)..];
        return result.EndsWith("```") ? result[..result.LastIndexOf("```", StringComparison.Ordinal)].Trim() : result.Trim();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
