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
    public List<string> SecondaryTopics { get; set; } = [];
    public double ClassificationConfidence { get; set; }
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
    public AcademicPaperBreakdown? PaperBreakdown { get; set; }
    public string? DocumentType { get; set; } // "Journal Article", "Conference Paper", "Review Article"
    public int WordCount { get; set; }
    public string? OpenAccessPdfUrl { get; set; }
    public string? DatabaseSource { get; set; } = "OpenAlex";

    /// <summary>
    /// How many times this paper has been cited.
    /// </summary>
    /// <remarks>
    /// OpenAlex reports it and Radar read it, used it to write the sentence "Cited 412 times."
    /// and then dropped the number. So the one structured signal of credibility the source
    /// gives us existed only inside a string, and nothing could sort on it - which is why
    /// Research Discovery returned results in whatever order OpenAlex happened to send them,
    /// under a spec that asks for credible and relevant research first.
    ///
    /// Also what separates a foundational paper from a merely old one. Both are twenty years
    /// past; one is still being cited.
    /// </remarks>
    public int CitationCount { get; set; }

    /// <summary>
    /// Where this paper sits in time: Recent, Established or Foundational.
    /// </summary>
    /// <remarks>
    /// On the item, not only inside the breakdown. The breakdown is a model call and is built
    /// only when somebody opens a paper, so the era - which costs nothing, needs no model, and
    /// is most useful while skimming a list of twenty - could not be shown on the one surface
    /// it was for. A reader would have had to open each paper to find out which of them was
    /// current, which is the opposite of the point.
    ///
    /// Derived, never typed. See ResearchRanking.EraOf.
    /// </remarks>
    public string? PaperEra { get; set; }

    // Enrichment fields — populated by the AI ingestion pipeline
    public ContentLayer Layer { get; set; } = ContentLayer.Ideas;
    public int CredibilityTier { get; set; }
    public string? IngestionSourceId { get; set; }
    public string? UrlHash { get; set; }
    public bool IsEnriched { get; set; }

    /// <summary>
    /// Whether the body Radar was given is actually about this headline.
    /// </summary>
    /// <remarks>
    /// False for an item that arrived through a whole-site feed carrying a daily podcast, where
    /// the title names one segment and the description is a rundown of the whole episode. Radar
    /// used to summarise the description and print it under the headline, which asserts something
    /// about an article the article does not say.
    ///
    /// Such an item keeps its title, source and link — all of which are real — and carries no
    /// summary, because Radar has not been given anything it can honestly say about it.
    ///
    /// True by default, including on every item ingested before this existed. Those were never
    /// checked, so this is not evidence that they are sound; it only means nothing has found them
    /// to be otherwise.
    /// </remarks>
    public bool BodyMatchesTitle { get; set; } = true;
    public Dictionary<string, string> PersonaImpact { get; set; } = [];
    public List<string> Opportunities { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
    public string? PersonalizedWhy { get; set; }
    public double RelevanceScore { get; set; }
    public double RelevanceConfidence { get; set; }
    public List<string> MatchedSignals { get; set; } = [];

    /// <summary>
    /// One line explaining why this user is seeing this item, computed next to the scorer that
    /// produced MatchedSignals. Server-side so the feed card and the home brief cannot disagree.
    /// </summary>
    public string? WhyYoureSeeingThis { get; set; }
    public string? NextMove { get; set; }
}

public class ChapterBreakdown
{
    public string Title { get; set; } = string.Empty;
    public TimeSpan Timestamp { get; set; }
    public string? Summary { get; set; }
}
