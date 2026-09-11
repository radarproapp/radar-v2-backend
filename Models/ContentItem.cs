namespace RadarV2.Models;

public class ContentItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ContentType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string WhatHappened { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceLogoUrl { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string AiSummary { get; set; } = string.Empty;
    public List<string> KeyInsights { get; set; } = [];
    public string WhyItMatters { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public DateTime PublishedAt { get; set; }
    public string? EstimatedReadTime { get; set; }
    public string? EstimatedWatchTime { get; set; }
    public bool IsSaved { get; set; }
    public string? ThumbnailUrl { get; set; }

    // Article-specific
    public string? Author { get; set; }

    // Podcast-specific
    public string? WhoShouldListen { get; set; }
    public List<string> KeyLessons { get; set; } = [];

    // Video-specific
    public List<ChapterBreakdown> Chapters { get; set; } = [];
    public List<string> KeyConcepts { get; set; } = [];

    // Podcast-episode-specific
    public string? AudioUrl { get; set; }
    public string? TranscriptText { get; set; }
    public int? DurationSeconds { get; set; }

    // Research paper-specific
    public List<string> Authors { get; set; } = [];
    public string? Journal { get; set; }
    public string? Doi { get; set; }
    public string? KeyFindings { get; set; }
    public string? Methodology { get; set; }
    public List<string> RelatedPaperIds { get; set; } = [];

    // Enrichment fields — populated by the AI ingestion pipeline
    public ContentLayer Layer { get; set; } = ContentLayer.Ideas;
    public int CredibilityTier { get; set; }
    public string? IngestionSourceId { get; set; }
    public string? UrlHash { get; set; }
    public bool IsEnriched { get; set; }
    public Dictionary<string, string> PersonaImpact { get; set; } = [];
    public List<string> Opportunities { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
}

public class ChapterBreakdown
{
    public string Title { get; set; } = string.Empty;
    public TimeSpan Timestamp { get; set; }
    public string? Summary { get; set; }
}
