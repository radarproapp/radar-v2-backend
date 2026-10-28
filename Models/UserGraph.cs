namespace RadarV2.Models;

/// <summary>
/// Everything Radar can honestly say a user has done.
/// </summary>
/// <remarks>
/// Computed from the source collections on every request, not stored. <c>UserProfile.Stats</c>
/// already has fields for most of this — ArticlesRead, PodcastsFinished, VideosWatched,
/// OpportunitiesApplied — and nothing in the codebase ever wrote to them, so the Growth Tracker
/// showed permanent zeros to everybody. A counter that can drift from the thing it counts will,
/// and here it drifted all the way to nothing.
///
/// Every number below traces to rows somebody's own actions created: analytics events, applied
/// opportunities, roadmap modules, playback heartbeats, answered quiz questions. Where there are
/// no rows the field is zero or the list is empty, and the client says so rather than filling in.
/// </remarks>
public class UserGraph
{
    /// <summary>The window this covers, so a total is never ambiguous.</summary>
    public DateTime? FirstActivityAt { get; set; }
    public DateTime? LastActivityAt { get; set; }

    public GraphListening Listening { get; set; } = new();
    public GraphLearning Learning { get; set; } = new();
    public GraphOpportunities Opportunities { get; set; } = new();
    public GraphReading Reading { get; set; } = new();

    /// <summary>Topics by how much attention they actually got, most first.</summary>
    public List<GraphTopic> TopTopics { get; set; } = [];

    /// <summary>Sources by how often their items were opened, most first.</summary>
    public List<GraphSource> TopSources { get; set; } = [];

    /// <summary>Month by month, so the shape of a year is visible rather than just its total.</summary>
    public List<GraphMonth> Months { get; set; } = [];

    /// <summary>
    /// Whether there is enough here to be worth showing. False for a new account, and the client
    /// shows what to do about it instead of a wall of zeros.
    /// </summary>
    public bool HasActivity =>
        Listening.SecondsPlayed > 0
        || Reading.ItemsOpened > 0
        // Building a path is activity. This counted only *completed* lessons, so somebody who
        // had just answered the three questions and had a section built was told "Nothing to
        // show yet — your graph fills itself in as you read, listen, build a path" on a page
        // that already knew they had five lessons across one section. Found on production, by
        // reading the two fields next to each other.
        || Learning.PathSections > 0
        || Learning.LessonsCompleted > 0
        || Learning.QuizzesTaken > 0
        || Learning.ExercisesCompleted > 0
        || Reading.ItemsSaved > 0
        || Reading.NotesWritten > 0
        || Reading.CapturesMade > 0
        || Opportunities.Saved > 0
        || Opportunities.Applied > 0;
}

public class GraphListening
{
    /// <summary>Time genuinely spent playing. Scrubbing to the end earns nothing.</summary>
    public double SecondsPlayed { get; set; }

    public int EpisodesStarted { get; set; }
    public int EpisodesFinished { get; set; }
    public int VideosStarted { get; set; }
    public int VideosFinished { get; set; }

    /// <summary>The one they gave the most time to, which is not always the one they finished.</summary>
    public GraphItem? MostPlayed { get; set; }
}

public class GraphLearning
{
    public int PathSections { get; set; }
    public int LessonsCompleted { get; set; }
    public int LessonsTotal { get; set; }
    public int QuizzesTaken { get; set; }

    /// <summary>Mean score across completed quizzes, or null when none are complete.</summary>
    public int? AverageQuizScore { get; set; }

    public int ProjectsCompleted { get; set; }

    /// <summary>Hands-on exercises done. Reading a tutorial and doing its lab are different acts.</summary>
    public int ExercisesCompleted { get; set; }

    /// <summary>Subjects the learner has sections on, in the order they added them.</summary>
    public List<string> Subjects { get; set; } = [];

    /// <summary>What the quizzes keep finding, deduplicated. Their words, not a diagnosis.</summary>
    public List<string> WeakAreas { get; set; } = [];
}

public class GraphOpportunities
{
    public int Saved { get; set; }
    public int Applied { get; set; }

    /// <summary>Applications by tracking status — interviewing, offered, closed.</summary>
    public Dictionary<string, int> ByStatus { get; set; } = [];
}

public class GraphReading
{
    public int ItemsOpened { get; set; }
    public int ItemsSaved { get; set; }
    public int PapersOpened { get; set; }
    public int NotesWritten { get; set; }
    public int CapturesMade { get; set; }
}

public class GraphTopic
{
    public string Topic { get; set; } = string.Empty;

    /// <summary>How many of their actions touched it.</summary>
    public int Interactions { get; set; }

    /// <summary>Time played on items in this topic, when any of them were media.</summary>
    public double SecondsPlayed { get; set; }
}

public class GraphSource
{
    public string Source { get; set; } = string.Empty;
    public int Opens { get; set; }
}

public class GraphMonth
{
    /// <summary>First day of the month, UTC.</summary>
    public DateTime Month { get; set; }

    public int Interactions { get; set; }
    public double SecondsPlayed { get; set; }
}

public class GraphItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Topic { get; set; }
    public ContentType Type { get; set; }
    public double SecondsPlayed { get; set; }
}
