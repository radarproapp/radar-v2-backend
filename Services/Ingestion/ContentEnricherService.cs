using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

public class ContentEnricherService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration     _config;
    private readonly RadarDatabase      _db;
    private readonly ILogger<ContentEnricherService> _log;

    public ContentEnricherService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        ILogger<ContentEnricherService> log)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _db          = db;
        _log         = log;
    }

    public async Task<ContentItem> EnrichAsync(RawFeedItem raw, CancellationToken ct)
    {
        var item = BuildBaseItem(raw);

        var apiKey = _config["OpenRouter:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogWarning("OpenRouter API key not set — skipping enrichment for {Title}", raw.Title);
            return item;
        }

        // Spend cap guard
        if (!await IsWithinSpendCapAsync(ct))
        {
            _log.LogWarning("OpenRouter monthly spend cap reached — skipping enrichment for '{Title}'.", raw.Title);
            return item;
        }

        try
        {
            var prompt = BuildPrompt(raw);
            var (result, usage) = await CallOpenRouterAsync(prompt, ct);
            if (result != null) ApplyEnrichment(item, result);
            item.IsEnriched = result != null;
            if (usage.HasValue) await RecordUsageAsync(usage.Value, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Enrichment failed for '{Title}': {Message}", raw.Title, ex.Message);
        }

        return item;
    }

    // ── Spend cap ─────────────────────────────────────────────────────────────

    private async Task<bool> IsWithinSpendCapAsync(CancellationToken ct)
    {
        var cap = _config.GetValue<decimal>("OpenRouter:MonthlySpendCapUsd", 25m);
        var month  = DateTime.UtcNow.ToString("yyyy-MM");
        var filter = Builders<IngestionQuotaDoc>.Filter.Eq(q => q.Id, $"openrouter:{month}");
        var doc    = await _db.IngestionQuotas.Find(filter).FirstOrDefaultAsync(ct);
        return doc is null || doc.SpendUsd < cap;
    }

    private async Task RecordUsageAsync((long InputTokens, long OutputTokens) usage, CancellationToken ct)
    {
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var id    = $"openrouter:{month}";
        // DeepSeek pricing (approximate, update if changed): $0.27/M input, $1.10/M output
        var spend = (usage.InputTokens / 1_000_000m * 0.27m) + (usage.OutputTokens / 1_000_000m * 1.10m);
        var filter = Builders<IngestionQuotaDoc>.Filter.Eq(q => q.Id, id);
        var update = Builders<IngestionQuotaDoc>.Update
            .SetOnInsert(q => q.Service, "openrouter")
            .SetOnInsert(q => q.Month, month)
            .Inc(q => q.RequestCount, 1)
            .Inc(q => q.TotalInputTokens,  usage.InputTokens)
            .Inc(q => q.TotalOutputTokens, usage.OutputTokens)
            .Inc(q => q.SpendUsd, spend)
            .Set(q => q.UpdatedAt, DateTime.UtcNow);
        await _db.IngestionQuotas.UpdateOneAsync(filter, update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    // ── Base item from raw feed ───────────────────────────────────────────────

    private static ContentItem BuildBaseItem(RawFeedItem raw) => new()
    {
        Id                = Guid.NewGuid().ToString(),
        Type              = raw.DefaultType,
        Layer             = raw.Layer,
        Title             = raw.Title,
        Source            = raw.SourceName,
        Url               = raw.Url,
        UrlHash           = raw.UrlHash,
        IngestionSourceId = raw.SourceId,
        CredibilityTier   = raw.Tier,
        Tags              = raw.Topics,
        PublishedAt       = raw.PublishedAt,
        AiSummary         = raw.Description,
        Signal            = raw.Title,
        WhatHappened      = raw.Description,
        Author            = raw.Author,
        Authors           = raw.Authors,
        AudioUrl          = raw.AudioUrl,
        TranscriptText    = raw.TranscriptText,
        DurationSeconds   = raw.DurationSeconds,
        EstimatedReadTime = raw.DurationSeconds.HasValue
            ? FormatDuration(raw.DurationSeconds.Value) : null,
        IsEnriched        = false,
    };

    private static string FormatDuration(int seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}h {ts.Minutes}m"
            : $"{ts.Minutes} min";
    }

    // ── Prompt templates ──────────────────────────────────────────────────────

    private static string BuildPrompt(RawFeedItem raw)
    {
        if (raw.DefaultType == ContentType.Podcast) return PodcastTemplate(raw);
        return raw.Layer switch
        {
            ContentLayer.Policy   => PolicyTemplate(raw),
            ContentLayer.Academic => AcademicTemplate(raw),
            ContentLayer.Ideas    => IdeasTemplate(raw),
            _                     => GenericTemplate(raw)
        };
    }

    private static string PolicyTemplate(RawFeedItem raw) => $$"""
        You are Radar's Policy Intelligence Engine. Transform this content into a structured Radar Policy Intelligence Brief.

        Source: {{raw.SourceName}} (Credibility Tier {{raw.Tier}})
        Title: {{raw.Title}}
        Content: {{Truncate(raw.Description, 1200)}}
        Topics: {{string.Join(", ", raw.Topics)}}

        Respond ONLY with valid JSON matching this exact structure:
        {
          "signal": "One-line thesis — what is this fundamentally trying to achieve? Max 20 words.",
          "whatHappened": "Executive TL;DR in 150-200 words. Plain language. Goals, mechanisms, scope, timeline.",
          "whyItMatters": "2-3 sentences on strategic significance for African users.",
          "keyInsights": ["Key provision or change 1", "Key provision or change 2", "Key provision or change 3", "Key provision or change 4"],
          "personaImpact": {
            "Student": "1-2 sentences on implications and opportunities for students.",
            "Founder": "1-2 sentences on implications and opportunities for entrepreneurs.",
            "Professional": "1-2 sentences on implications for working professionals.",
            "Policymaker": "1-2 sentences on implications for government and policy practitioners."
          },
          "opportunities": ["Specific opportunity 1 for African users", "Specific opportunity 2"],
          "recommendedActions": ["Concrete action you can take this week", "Action 2", "Action 3"],
          "tags": ["Tag1", "Tag2", "Tag3"]
        }
        """;

    private static string AcademicTemplate(RawFeedItem raw) => $$"""
        You are Radar's Research Intelligence Engine. Transform this academic content into a Radar Research Brief.

        Source: {{raw.SourceName}}
        Title: {{raw.Title}}
        Authors: {{string.Join(", ", raw.Authors)}}
        Abstract: {{Truncate(raw.Description, 1200)}}
        Topics: {{string.Join(", ", raw.Topics)}}

        Respond ONLY with valid JSON matching this exact structure:
        {
          "signal": "One-line finding — the single most important result. Max 20 words.",
          "whatHappened": "30-second summary in plain English. 100-150 words. Accessible to a non-expert.",
          "whyItMatters": "2-3 sentences on why this research matters practically, especially for Africa.",
          "keyInsights": ["Key finding 1", "Key finding 2", "Key finding 3", "Methodological note or limitation"],
          "personaImpact": {
            "Student": "How this is relevant for students and researchers in this field.",
            "Founder": "Business or startup application of this research.",
            "Professional": "Practical implication for practitioners.",
            "Policymaker": "Policy implications or evidence for decision-makers."
          },
          "opportunities": ["Research gap or application opportunity 1", "Opportunity 2"],
          "recommendedActions": ["Action based on this research 1", "Action 2"],
          "tags": ["Tag1", "Tag2", "Tag3"]
        }
        """;

    private static string IdeasTemplate(RawFeedItem raw) => $$"""
        You are Radar's Ideas Intelligence Engine. Transform this essay or article into a Radar Ideas Brief.

        Source: {{raw.SourceName}}
        Title: {{raw.Title}}
        Content: {{Truncate(raw.Description, 1200)}}

        Respond ONLY with valid JSON matching this exact structure:
        {
          "signal": "One-sentence thesis — the central idea. Max 20 words.",
          "whatHappened": "3-minute summary of core arguments and evidence. 150-200 words.",
          "whyItMatters": "2-3 sentences on why this idea matters for African professionals, students, and founders.",
          "keyInsights": ["Core argument or insight 1", "Insight 2", "Insight 3", "Counterargument or limitation"],
          "personaImpact": {
            "Student": "How this idea changes how a student should think or act.",
            "Founder": "Strategic or business implication for founders.",
            "Professional": "Career or professional development angle.",
            "Policymaker": "Governance or policy relevance."
          },
          "opportunities": ["Practical opportunity arising from this idea 1", "Opportunity 2"],
          "recommendedActions": ["Action this week based on the idea", "Action 2", "Action 3"],
          "tags": ["Tag1", "Tag2", "Tag3"]
        }
        """;

    private static string PodcastTemplate(RawFeedItem raw)
    {
        var content = !string.IsNullOrWhiteSpace(raw.TranscriptText)
            ? $"Full Transcript:\n{Truncate(raw.TranscriptText, 3000)}"
            : $"Episode Description:\n{Truncate(raw.Description, 1200)}";

        var duration = raw.DurationSeconds.HasValue
            ? $"{raw.DurationSeconds.Value / 60} minutes"
            : "unknown duration";

        return $$"""
            You are Radar's Podcast Intelligence Engine. Transform this podcast episode into a structured Radar Podcast Brief.

            Podcast: {{raw.SourceName}}
            Episode: {{raw.Title}}
            Duration: {{duration}}
            {{content}}

            Respond ONLY with valid JSON matching this exact structure:
            {
              "signal": "One-line thesis — the single biggest idea or argument in this episode. Max 20 words.",
              "whatHappened": "3-minute listen summary in 150-200 words. Accessible to a newcomer. Cover main arguments, guests, and evidence.",
              "whyItMatters": "2-3 sentences on practical relevance for young African professionals, students, or founders.",
              "keyInsights": ["Core argument or lesson 1", "Lesson 2", "Lesson 3", "Lesson 4"],
              "personaImpact": {
                "Student": "How this episode helps a student — specific lens or insight.",
                "Founder": "Business or startup implication from the episode.",
                "Professional": "Career or sector relevance for working professionals.",
                "Policymaker": "Governance or systemic relevance."
              },
              "opportunities": ["Concrete opportunity or action this episode surfaces 1", "Opportunity 2"],
              "recommendedActions": ["Action this week based on the episode", "Action 2", "Action 3"],
              "whoShouldListen": "One sentence describing the ideal listener for this episode.",
              "keyLessons": ["Memorable lesson 1", "Lesson 2", "Lesson 3"],
              "tags": ["Tag1", "Tag2", "Tag3"]
            }
            """;
    }

    private static string GenericTemplate(RawFeedItem raw) => $$"""
        You are Radar's Intelligence Engine. Transform this content into a Radar Intelligence Brief.

        Source: {{raw.SourceName}}
        Title: {{raw.Title}}
        Content: {{Truncate(raw.Description, 1200)}}

        Respond ONLY with valid JSON:
        {
          "signal": "One-line signal or thesis. Max 20 words.",
          "whatHappened": "Clear 100-150 word summary.",
          "whyItMatters": "2-3 sentences on relevance and importance.",
          "keyInsights": ["Insight 1", "Insight 2", "Insight 3"],
          "personaImpact": { "Student": "...", "Founder": "...", "Professional": "...", "Policymaker": "..." },
          "opportunities": ["Opportunity 1", "Opportunity 2"],
          "recommendedActions": ["Action 1", "Action 2"],
          "tags": ["Tag1", "Tag2"]
        }
        """;

    // ── OpenRouter call ───────────────────────────────────────────────────────

    private async Task<(EnrichmentResult? Result, (long Input, long Output)? Usage)> CallOpenRouterAsync(string prompt, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new
        {
            model           = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat",
            messages        = new[] { new { role = "user", content = prompt } },
            response_format = new { type = "json_object" },
            temperature     = 0.3
        });

        var client = _httpFactory.CreateClient("OpenRouter");
        var request = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return (null, null);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        // Extract token usage for spend tracking
        (long Input, long Output)? usage = null;
        if (doc.RootElement.TryGetProperty("usage", out var u))
        {
            var input  = u.TryGetProperty("prompt_tokens",     out var pt) ? pt.GetInt64() : 0L;
            var output = u.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt64() : 0L;
            usage = (input, output);
        }

        if (string.IsNullOrWhiteSpace(content)) return (null, usage);

        // Strip markdown code fences if model wraps output
        content = content.Trim();
        if (content.StartsWith("```")) content = content.Split('\n', 2)[1];
        if (content.EndsWith("```")) content = content[..content.LastIndexOf("```")];

        using var result = JsonDocument.Parse(content.Trim());
        return (ParseEnrichmentResult(result.RootElement), usage);
    }

    private static EnrichmentResult ParseEnrichmentResult(JsonElement el)
    {
        static string Str(JsonElement e, string key) =>
            e.TryGetProperty(key, out var v) ? v.GetString() ?? string.Empty : string.Empty;

        static List<string> Arr(JsonElement e, string key)
        {
            if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return [];
            return v.EnumerateArray().Select(i => i.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList();
        }

        static Dictionary<string, string> Dict(JsonElement e, string key)
        {
            if (!e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Object) return [];
            return v.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
        }

        return new EnrichmentResult
        {
            Signal             = Str(el, "signal"),
            WhatHappened       = Str(el, "whatHappened"),
            WhyItMatters       = Str(el, "whyItMatters"),
            KeyInsights        = Arr(el, "keyInsights"),
            PersonaImpact      = Dict(el, "personaImpact"),
            Opportunities      = Arr(el, "opportunities"),
            RecommendedActions = Arr(el, "recommendedActions"),
            Tags               = Arr(el, "tags"),
            WhoShouldListen    = Str(el, "whoShouldListen"),
            KeyLessons         = Arr(el, "keyLessons"),
        };
    }

    private static void ApplyEnrichment(ContentItem item, EnrichmentResult r)
    {
        if (!string.IsNullOrWhiteSpace(r.Signal))             item.Signal             = r.Signal;
        if (!string.IsNullOrWhiteSpace(r.WhatHappened))       item.WhatHappened       = r.WhatHappened;
        if (!string.IsNullOrWhiteSpace(r.WhyItMatters))       item.WhyItMatters       = r.WhyItMatters;
        if (r.KeyInsights.Count > 0)                          item.KeyInsights        = r.KeyInsights;
        if (r.PersonaImpact.Count > 0)                        item.PersonaImpact      = r.PersonaImpact;
        if (r.Opportunities.Count > 0)                        item.Opportunities      = r.Opportunities;
        if (r.RecommendedActions.Count > 0)                   item.RecommendedActions = r.RecommendedActions;
        if (r.Tags.Count > 0)                                 item.Tags               = r.Tags;
        // Podcast-specific
        if (!string.IsNullOrWhiteSpace(r.WhoShouldListen))    item.WhoShouldListen    = r.WhoShouldListen;
        if (r.KeyLessons.Count > 0)                           item.KeyLessons         = r.KeyLessons;
        item.AiSummary = r.WhatHappened;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private class EnrichmentResult
    {
        public string Signal { get; set; } = string.Empty;
        public string WhatHappened { get; set; } = string.Empty;
        public string WhyItMatters { get; set; } = string.Empty;
        public List<string> KeyInsights { get; set; } = [];
        public Dictionary<string, string> PersonaImpact { get; set; } = [];
        public List<string> Opportunities { get; set; } = [];
        public List<string> RecommendedActions { get; set; } = [];
        public List<string> Tags { get; set; } = [];
        // Podcast-specific
        public string WhoShouldListen { get; set; } = string.Empty;
        public List<string> KeyLessons { get; set; } = [];
    }
}
