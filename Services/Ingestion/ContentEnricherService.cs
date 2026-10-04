using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services.Ingestion;

public class ContentEnricherService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration     _config;
    private readonly RadarDatabase      _db;
    private readonly ILogger<ContentEnricherService> _log;
    private readonly IAiEngine _ai;

    public ContentEnricherService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        RadarDatabase db,
        ILogger<ContentEnricherService> log,
        IAiEngine ai)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _db          = db;
        _log         = log;
        _ai          = ai;
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
            var result = await _ai.GenerateJsonAsync<EnrichmentResult>(
                "content-enrichment",
                "You are Radar's content intelligence engine. Return only valid JSON matching the requested schema. Choose a precise primary topic and up to four secondary topics from Finance, Law, Climate, Technology, Business, Policy, Education, Science, Health, Agriculture, Energy, Career, Arts, Sports, or Society. Tags must describe the actual subject, not merely a keyword in the source.",
                BuildPrompt(raw),
                ct);
            if (result != null) ApplyEnrichment(item, result);
            item.IsEnriched = result != null;
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
        // The source registry curates a topic set per feed (e.g. Premium Times → Africa, Nigeria,
        // Governance, Legal). Persist it as the item's topic set instead of discarding it: without
        // this, an item's only visible category is its coarse source Layer ("Law"), which is why
        // general-interest pieces were shown under the wrong heading. The first curated topic is the
        // primary; the rest become secondary topics so the UI can render "Climate · Finance · Policy".
        Topic             = raw.Topics.FirstOrDefault() ?? string.Empty,
        SecondaryTopics   = raw.Topics.Skip(1).ToList(),
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

    private static void ApplyEnrichment(ContentItem item, EnrichmentResult r)
    {
        if (!string.IsNullOrWhiteSpace(r.Signal))             item.Signal             = r.Signal;
        if (!string.IsNullOrWhiteSpace(r.WhatHappened))       item.WhatHappened       = r.WhatHappened;
        if (!string.IsNullOrWhiteSpace(r.WhyItMatters))       item.WhyItMatters       = r.WhyItMatters;
        if (r.KeyInsights.Count > 0)                          item.KeyInsights        = r.KeyInsights;
        if (r.PersonaImpact.Count > 0)                        item.PersonaImpact      = r.PersonaImpact;
        if (r.Opportunities.Count > 0)                        item.Opportunities      = r.Opportunities;
        if (r.RecommendedActions.Count > 0)                   item.RecommendedActions = r.RecommendedActions;
        if (r.Tags.Count > 0)
        {
            item.Tags = r.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
            item.Topic = item.Tags[0];
            item.SecondaryTopics = item.Tags.Skip(1).ToList();
            item.ClassificationConfidence = .8;
        }
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
