namespace RadarV2.Services;

/// <summary>
/// Which model answers which kind of question.
/// </summary>
/// <remarks>
/// Every call went to one model, named once in configuration. That is one decision covering
/// three very different jobs, so it was wrong for two of them: deciding whether an uploaded file
/// is a CV is a sentence of work and waits behind a model chosen for writing a five-hundred-word
/// summary of a research paper, while marking somebody's project out of ten - the one call where
/// being wrong costs a person something - gets exactly the same care as tagging an RSS item.
///
/// Three tiers, named for the job rather than the vendor, so replacing a model is editing one
/// line and not re-reading every call site.
///
/// <list type="bullet">
/// <item><b>Fast</b> - short, structured, high volume, and somebody is usually waiting. Deciding
/// a document's kind, tagging an ingested item, writing one "why this matters" line. These are
/// most of the calls Radar makes and almost none of its thinking.</item>
/// <item><b>Balanced</b> - real writing and real judgement, read by a learner and judged by
/// them. Tutoring, a paper summary, a section of a learning path.</item>
/// <item><b>Careful</b> - the few calls where a wrong answer costs somebody something. A mark on
/// a CV, a project refused at five out of ten that deserved seven. Rare enough that the price
/// hardly registers, and the only place where it buys anything.</item>
/// </list>
///
/// Every tier and every per-feature override is configuration, so a route can be moved from
/// Railway without a deploy - which matters because the right answer here is found by measuring,
/// not by arguing, and an experiment that needs a release is an experiment nobody runs.
/// </remarks>
public class ModelRouter
{
    /// <summary>What a feature needs, as opposed to which model currently provides it.</summary>
    public enum Tier
    {
        Fast,
        Balanced,
        Careful,
    }

    private readonly IConfiguration _config;

    public ModelRouter(IConfiguration config) => _config = config;

    /// <summary>
    /// Short, structured, and usually blocking something: classification, tagging, one-line
    /// explanations. The bulk of Radar's calls by count.
    /// </summary>
    public static readonly IReadOnlySet<string> FastWork = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Runs over every ingested item on a cron. The highest-volume call in the product by a
        // wide margin, and nobody is waiting for any single one of them.
        "content-enrichment",

        // One paragraph per feed item per reader.
        "personalized-why",
        "capture-why",

        // Is this a CV or an essay? One sentence of work that gates a longer call.
        "document-kind",

        // Is this video worth putting in the lesson?
        "lesson-relevance",

        // Scoring an advert or a feed item against one reader. Both run per item per person,
        // and both have a deterministic scorer underneath them that already did the work.
        "opportunity-matching",
        "feed-relevance",
    };

    /// <summary>
    /// Writing and judgement a learner reads and forms an opinion about.
    /// </summary>
    public static readonly IReadOnlySet<string> BalancedWork = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mentor-chat",
        "ask-radar",
        "academic-paper-breakdown",
        "learning-pathway",
        "learning-section",
        "document-subject",
        "project-plan",
        "research-gaps",
        "research-explain",
    };

    /// <summary>
    /// The calls that put a number or a verdict on a person's work.
    /// </summary>
    /// <remarks>
    /// Low volume and high consequence, which is the only combination that repays the most
    /// expensive tier. A project is accepted at six out of ten; one mark either way is the
    /// difference between somebody finishing and somebody giving up.
    /// </remarks>
    public static readonly IReadOnlySet<string> CarefulWork = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "document-assessment",
        "project-assessment",
    };

    /// <summary>Which tier a feature belongs to.</summary>
    /// <remarks>
    /// An unlisted feature is Balanced rather than Fast. A call nobody has classified is more
    /// likely to be real work than trivia, and the cost of being wrong in that direction is a
    /// few cents; the other direction is a bad answer in front of a learner.
    /// <see cref="ModelRouterTests"/> asserts every feature the codebase actually uses is listed,
    /// so this default covers a new call for one commit, not forever.
    /// </remarks>
    public static Tier TierFor(string feature)
    {
        if (FastWork.Contains(feature)) return Tier.Fast;
        if (CarefulWork.Contains(feature)) return Tier.Careful;

        return Tier.Balanced;
    }

    /// <summary>
    /// The model to send this feature to.
    /// </summary>
    /// <remarks>
    /// Resolved in three steps, most specific first. A per-feature override pins one route
    /// without disturbing its tier - which is how a single misbehaving call gets moved while a
    /// measurement is still running. Then the tier's model. Then the single model the whole
    /// product used before this existed, which is what every tier falls back to when none of
    /// this is configured, so an unconfigured deployment behaves exactly as it did.
    /// </remarks>
    public string For(string feature)
    {
        var pinned = _config[$"OpenRouter:Models:Features:{feature}"];
        if (!string.IsNullOrWhiteSpace(pinned)) return pinned;

        var tier = _config[$"OpenRouter:Models:{TierFor(feature)}"];
        if (!string.IsNullOrWhiteSpace(tier)) return tier;

        return Fallback;
    }

    /// <summary>
    /// The one model everything used before there were tiers.
    /// </summary>
    /// <remarks>
    /// Kept as the floor rather than hardcoding a vendor per tier. A deployment that sets none
    /// of the tier keys gets precisely its old behaviour, so this can ship dark and be turned on
    /// one tier at a time.
    /// </remarks>
    public string Fallback => _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat";
}
