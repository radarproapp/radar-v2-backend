namespace RadarV2.Models;

/// <summary>
/// Everything the signed-in home screen shows, in one payload.
///
/// This exists because the previous professional home was a hand-written mock: career options,
/// salary bands and employer names were string literals in the React component, identical for
/// every user. The reviewer's reaction — "when I picked cybersecurity, how do all these relate?"
/// — was correct, because nothing on that screen was derived from them.
///
/// Every field here is computed from the user's own profile, roadmap, behaviour and the real
/// content Radar ingested. Anything Radar cannot ground is left null so the UI can omit it,
/// rather than filled with plausible-sounding placeholder copy.
/// </summary>
public class HomeBrief
{
    /// <summary>One line of grounded context: role, goal and location as the user gave them.</summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>The three items that matter most to this user today. May be empty.</summary>
    public List<HomeSignal> TopSignals { get; set; } = [];

    /// <summary>What Radar has observed the user actually engaging with. May be empty.</summary>
    public List<NoticedSignal> Noticed { get; set; } = [];

    /// <summary>The specific thing to learn today — never "continue your learning path".</summary>
    public HomeFocus? TodaysFocus { get; set; }

    /// <summary>The specific artifact to build next, named concretely.</summary>
    public HomeBuild? NextBuild { get; set; }

    /// <summary>Top matched opportunities. May be empty.</summary>
    public List<HomeOpportunity> Opportunities { get; set; } = [];

    public UserStats Stats { get; set; } = new();
}

/// <summary>One piece of intelligence on the home screen, with the reason it is being shown.</summary>
public class HomeSignal
{
    public string ContentItemId { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public ContentLayer Layer { get; set; }
    public string? Topic { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime PublishedAt { get; set; }

    /// <summary>Short, specific reason this user is seeing this item. Never a generic sentence.</summary>
    public string WhyYoureSeeingThis { get; set; } = string.Empty;
}

/// <summary>A thing Radar has watched the user return to, and what to do about it.</summary>
public class NoticedSignal
{
    public string Term { get; set; } = string.Empty;
    public int TimesEngaged { get; set; }
    public string Suggestion { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = string.Empty;
}

/// <summary>The named topic to study today, drawn from the active roadmap's current stage.</summary>
public class HomeFocus
{
    public string Topic { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Why { get; set; } = string.Empty;
    public string? EstimatedTime { get; set; }
    public string ActionUrl { get; set; } = "/learn";
}

/// <summary>The named artifact to build next.</summary>
public class HomeBuild
{
    public string Artifact { get; set; } = string.Empty;
    public string Why { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = "/projects";
}

/// <summary>An opportunity plus the specific reason it matches this user.</summary>
public class HomeOpportunity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Organisation { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int MatchScorePercent { get; set; }
    public string MatchTier { get; set; } = string.Empty;
    public int? DaysUntilDeadline { get; set; }
    public string WhyItMatches { get; set; } = string.Empty;
}
