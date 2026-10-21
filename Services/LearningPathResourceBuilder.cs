using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Builds learning resources from the user's own learning path, one per lesson.
///
/// The catalogue approach this replaces could only ever serve the domains someone had hand-written:
/// seven pools of hardcoded video IDs, with everything else falling through to software engineering
/// or, after that was fixed, to nothing at all. A nurse manager, a lawyer or an agronomist got an
/// empty Learn page. Tutorials have to follow whatever path the learner actually has, not the paths
/// we happened to anticipate.
///
/// So resources are derived from the roadmap's lessons. Each lesson becomes a tutorial scoped to a
/// channel that teaches that domain well, and each stage gets one "what changed recently" entry,
/// giving roughly the 80/20 split between learning the skill and staying current.
///
/// Links are searches, not fixed video IDs. A channel search always resolves to that channel's
/// current videos on the topic; a hardcoded ID rots silently and cannot be reviewed for accuracy.
/// Channels come from handles verified by loading their pages (see LEARNING_CHANNELS.md); when a
/// domain has none, the link is an unscoped YouTube search, which still works for any subject on
/// earth rather than leaving the page empty.
/// </summary>
public static class LearningPathResourceBuilder
{
    private sealed record ChannelSet(string[] Keywords, string[] Handles);

    /// <summary>Verified handles only. Adding a domain is one row.</summary>
    private static readonly ChannelSet[] Channels =
    [
        new(["cyber", "security", "soc", "hacking", "threat", "malware", "forensic", "penetration", "siem"],
            ["NetworkChuck", "professormesser", "davidbombal", "MyDFIR", "LetsDefend", "_JohnHammond", "13cubed", "HackerSploit", "ippsec"]),
        new(["data", "analytics", "sql", "excel", "power bi", "statistic", "dashboard"],
            ["AlexTheAnalyst", "LukeBarousse", "techTFQ", "GuyInACube", "LeilaGharani", "excelisfun"]),
        new(["software", "engineer", "developer", "coding", "programming", "backend", "frontend", "web"],
            ["freecodecamp", "TraversyMedia", "Fireship", "TechWithTim", "NeetCode", "ThePrimeagen"]),
        new(["ai", "machine learning", "deep learning", "llm", "neural"],
            ["3blue1brown", "statquest", "sentdex", "Deeplearningai", "AndrejKarpathy"]),
        new(["product", "pm", "ux", "roadmap"],
            ["ProductSchoolSanFrancisco", "LennysPodcast"]),
        new(["finance", "fintech", "valuation", "banking", "invest", "accounting"],
            ["CorporateFinanceInstitute", "TheFinanceStoryteller", "AswathDamodaranonValuation", "PatrickBoyle"]),
        new(["entrepreneur", "startup", "business", "venture", "founder"],
            ["ycombinator", "StartupGrind", "Slidebean"]),
        new(["policy", "govern", "regulation", "public"],
            ["CSIS", "BrookingsInstitution"]),
        new(["marketing", "sales", "growth", "brand", "customer"],
            ["HubSpotMarketing"]),
        new(["math", "economics", "science", "physics", "chemistry", "biology", "statistics"],
            ["khanacademy", "crashcourse", "TEDEd"]),
        new(["cloud", "devops", "infrastructure", "kubernetes", "architecture"],
            ["IBMTechnology", "freecodecamp"]),
    ];

    // No "general teaching channels" fallback on purpose. Scoping a nursing-management query to
    // Khan Academy returns nothing, which is worse than an unscoped search: the learner sees an
    // empty channel page and concludes Radar has nothing for them. When no domain matches, search
    // all of YouTube - it covers every subject, which a curated shortlist never will.

    /// <param name="resolved">
    /// Real videos already found for a lesson title, keyed by that title. Supplied from the
    /// YouTube cache when available; when it is empty every resource falls back to a search link,
    /// which still works and costs no quota.
    /// </param>
    public static List<LearningResource> Build(
        GrowthRoadmap roadmap, IReadOnlyDictionary<string, YouTubeVideoRef>? resolved = null)
    {
        var subject = SubjectPhrase.Clean(
            !string.IsNullOrWhiteSpace(roadmap.Goal) ? roadmap.Goal : roadmap.Interest);

        var handles = HandlesFor($"{roadmap.Goal} {roadmap.Interest} {roadmap.Title}");
        var resources = new List<LearningResource>();

        foreach (var module in roadmap.Modules)
        {
            var order = 0;
            var lessons = module.Lessons.OrderBy(l => l.Order).ToList();

            foreach (var lesson in lessons)
            {
                order++;
                // The lesson this one leads to, so the "why" can name it rather than assert that
                // one exists. Null on the last lesson of a module, which is its own answer.
                var nextLessonTitle = order < lessons.Count ? lessons[order].Title : null;
                var handle = handles.Length > 0 ? handles[(order - 1) % handles.Length] : null;
                var query = !string.IsNullOrWhiteSpace(lesson.SearchHint)
                    ? lesson.SearchHint!
                    : BuildQuery(lesson.Title, subject);

                var match = resolved is not null && resolved.TryGetValue(lesson.Title, out var found) ? found : null;

                resources.Add(new LearningResource
                {
                    Id = $"lp-{Slug(module.Level)}-{order:00}",
                    Order = order,
                    Level = module.Level,
                    SequenceRole = order == 1 ? "Start here" : order == lessons.Count ? "Then" : "Next",
                    // A resolved video shows its real title; otherwise the lesson name stands in and
                    // the link is a search, so the card never claims to be a video it has not found.
                    Title = match?.Title ?? lesson.Title,
                    Url = match?.Url ?? SearchUrl(handle, query),
                    EmbedUrl = match?.EmbedUrl,
                    ThumbnailUrl = match?.ThumbnailUrl,
                    Source = match is not null ? $"YouTube · {match.ChannelTitle}"
                        : handle is null ? "YouTube" : $"YouTube · {handle}",
                    ChannelOrAuthor = match?.ChannelTitle ?? handle,
                    Duration = lesson.EstimatedTime,
                    EstimatedTime = lesson.EstimatedTime,
                    Type = LearningResourceType.TutorialVideo,
                    WhyAmILearningThis = WhyLesson(lesson.Title, module.Level, subject, nextLessonTitle, module.Project?.Deliverable),
                    WhatYouWillBuild = module.Project?.Deliverable ?? $"Working knowledge of {SubjectPhrase.Lower(lesson.Title)}.",
                    PracticeExerciseTitle = $"Practice: {lesson.Title}",
                    PracticeExercise = $"Follow along with the video, then redo it yourself without pausing it.",
                    QuizTopic = lesson.Title,
                    WhyItMatters = WhyLesson(lesson.Title, module.Level, subject, nextLessonTitle, module.Project?.Deliverable),
                    NextMove = $"1. Watch one video on {SubjectPhrase.Lower(lesson.Title)}.\n2. Redo it yourself without the video.\n3. Write down the part you had to look up.",
                });
            }

            // What this stage is for. Radar Learn is "building skills and demonstration of
            // capabilities by learning and building projects at the same time", and a path made
            // only of videos demonstrates nothing - so the module's project is a resource in its
            // own right rather than a footnote under the last lesson.
            if (module.Project is not null && !string.IsNullOrWhiteSpace(module.Project.Title))
            {
                resources.Add(new LearningResource
                {
                    Id = $"lp-{Slug(module.Level)}-project",
                    Order = lessons.Count + 1,
                    Level = module.Level,
                    SequenceRole = "Build",
                    Title = module.Project.Title,
                    Url = string.Empty,
                    Source = "Radar project",
                    Duration = module.Project.EstimatedTime,
                    EstimatedTime = module.Project.EstimatedTime,
                    Type = LearningResourceType.Project,
                    WhyAmILearningThis = string.IsNullOrWhiteSpace(module.Project.WhyItProvesCapability)
                        ? $"Proves you can do {subject} rather than only describe it."
                        : module.Project.WhyItProvesCapability,
                    WhatYouWillBuild = module.Project.Deliverable,
                    PracticeExerciseTitle = module.Project.Title,
                    PracticeExercise = string.Join("\n", module.Project.Steps),

                    // Spec 5.5 asks for how-to material in step-by-step form. These are real
                    // authored steps from the stage project, so they become real steps rather
                    // than a newline-joined paragraph. Tutorial *videos* get none: nobody has
                    // watched them, and inventing the steps of a video is the confident
                    // fabrication the rest of this work has been removing. That half waits on
                    // transcripts (5e.8).
                    StepByStepBreakdown = module.Project.Steps
                        .Select((step, index) => new TutorialStep
                        {
                            StepNumber = index + 1,
                            Title = $"Step {index + 1}",
                            Instruction = step,
                        })
                        .ToList(),
                    QuizTopic = module.Project.Title,
                    WhyItMatters = module.Project.WhyItProvesCapability ?? string.Empty,
                    NextMove = module.Project.Steps.FirstOrDefault() ?? "Start the first step.",
                });
            }

            // The 20%: what changed recently in this subject. Sorted by upload date, so it stays
            // current without anyone maintaining a list.
            resources.Add(new LearningResource
            {
                Id = $"lp-{Slug(module.Level)}-current",
                Order = order + 1,
                Level = module.Level,
                SequenceRole = "Stay current",
                Title = $"What changed recently in {subject}",
                Url = RecentSearchUrl(BuildQuery($"{subject} new", subject)),
                Source = "YouTube",
                Duration = "15 min",
                EstimatedTime = "15 min",
                Type = LearningResourceType.NewDevelopmentVideo,
                WhyAmILearningThis = $"Tools and practice in {subject} move. This keeps what you are learning current.",
                WhatYouWillBuild = "An up to date view of what practitioners are using now.",
                QuizTopic = $"{subject} developments",
                WhyItMatters = $"Learning the fundamentals is not enough if the tools changed last year. Check this once a week.",
                NextMove = $"1. Watch one recent update on {SubjectPhrase.Lower(subject)}.\n2. Note anything that changes how you would do your current stage.\n3. Add it to your notes.",
            });
        }

        return resources;
    }

    /// <summary>The search each lesson would run, so a background pass can resolve them.</summary>
    public static Dictionary<string, string> QueriesFor(GrowthRoadmap roadmap)
    {
        var subject = SubjectPhrase.Clean(
            !string.IsNullOrWhiteSpace(roadmap.Goal) ? roadmap.Goal : roadmap.Interest);

        return roadmap.Modules
            .SelectMany(m => m.Lessons)
            .Where(l => !string.IsNullOrWhiteSpace(l.Title))
            .GroupBy(l => l.Title)
            .ToDictionary(
                g => g.Key,
                g => !string.IsNullOrWhiteSpace(g.First().SearchHint)
                    ? g.First().SearchHint!
                    : BuildQuery(g.Key, subject));
    }

    private static string[] HandlesFor(string text)
    {
        var haystack = (text ?? string.Empty).ToLowerInvariant();
        return Channels.FirstOrDefault(c => c.Keywords.Any(haystack.Contains))?.Handles ?? [];
    }

    /// <summary>
    /// Lesson titles are written for humans ("TCP/IP, DNS and HTTP: how traffic actually moves").
    /// Strip the prose so the search is about the subject, and add the domain when the title alone
    /// would be ambiguous.
    /// </summary>
    private static string BuildQuery(string lessonTitle, string subject)
    {
        var title = lessonTitle ?? string.Empty;

        var colon = title.IndexOf(':');
        if (colon > 3) title = title[..colon];

        title = title.Replace(" & ", " ").Replace("&", " ").Trim();

        // Short or generic titles need the subject for context; specific ones do not.
        var needsContext = title.Split(' ').Length <= 3
            || title.Contains("fundamentals", StringComparison.OrdinalIgnoreCase)
            || title.Contains("basics", StringComparison.OrdinalIgnoreCase)
            || title.Contains("introduction", StringComparison.OrdinalIgnoreCase);

        var query = needsContext ? $"{subject} {title}" : title;
        return $"{query} tutorial".Trim();
    }

    private static string SearchUrl(string? handle, string query) =>
        handle is null
            ? $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}"
            : $"https://www.youtube.com/@{handle}/search?query={Uri.EscapeDataString(query)}";

    /// <summary>YouTube's "sort by upload date" parameter, for the stay-current entries.</summary>
    private static string RecentSearchUrl(string query) =>
        $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&sp=CAI%253D";

    /// <summary>
    /// Why this lesson, for this learner, in this position.
    ///
    /// It used to end "you need X before the next step makes sense" on every lesson, without
    /// knowing what the next step was - the same sentence on every card, asserting a dependency
    /// nothing had checked. The builder has the ordered list and the module's project in hand, so
    /// it can name the thing the lesson actually leads to.
    ///
    /// Spec 5.6 asks that each resource answer this "against the user's goal". It does, by
    /// construction: `subject` is derived from `roadmap.Goal`, so naming the subject names the
    /// goal. An earlier version also prefixed "Your goal is X" when the two differed - but since
    /// one is built from the other, that condition was false in every real case, and a branch
    /// that cannot fire is worse than no branch.
    /// </summary>
    private static string WhyLesson(
        string lessonTitle,
        string stage,
        string subject,
        string? nextLessonTitle,
        string? projectDeliverable)
    {
        if (!string.IsNullOrWhiteSpace(nextLessonTitle))
            return $"You need {SubjectPhrase.Lower(lessonTitle)} before {SubjectPhrase.Lower(nextLessonTitle)}.";

        if (!string.IsNullOrWhiteSpace(projectDeliverable))
            return $"This is the last {stage.ToLowerInvariant()} lesson before you build {SubjectPhrase.Lower(projectDeliverable)}.";

        return $"Part of your {stage.ToLowerInvariant()} stage in {subject}.";
    }

    private static string Slug(string value) =>
        new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
