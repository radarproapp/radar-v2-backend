using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Which model answers which kind of call.
/// </summary>
/// <remarks>
/// Every call went to one model named once in configuration - one decision covering three very
/// different jobs, and so wrong for two of them. Deciding whether an upload is a CV is a
/// sentence of work and waited behind a model picked for writing five hundred words about a
/// research paper; marking somebody's project out of ten, the one call where being wrong costs
/// a person something, got exactly the care given to tagging an RSS item.
/// </remarks>
public class ModelRouterTests
{
    private static ModelRouter Router(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

    // ── the tiers ────────────────────────────────────────────────────────────

    /// <summary>The high-volume, short, blocking calls.</summary>
    [Theory]
    [InlineData("content-enrichment")]
    [InlineData("document-kind")]
    [InlineData("personalized-why")]
    [InlineData("capture-why")]
    [InlineData("lesson-relevance")]
    [InlineData("opportunity-matching")]
    [InlineData("feed-relevance")]
    public void ShortStructuredWorkGoesToTheFastTier(string feature)
    {
        Assert.Equal(ModelRouter.Tier.Fast, ModelRouter.TierFor(feature));
    }

    /// <summary>The few calls that put a verdict on a person's work.</summary>
    /// <remarks>
    /// Low volume and high consequence, which is the only combination that repays the most
    /// expensive tier. A project is accepted at six out of ten; one mark either way is the
    /// difference between somebody finishing and somebody giving up.
    /// </remarks>
    [Theory]
    [InlineData("document-assessment")]
    [InlineData("project-assessment")]
    public void MarkingSomebodysWorkGoesToTheCarefulTier(string feature)
    {
        Assert.Equal(ModelRouter.Tier.Careful, ModelRouter.TierFor(feature));
    }

    [Theory]
    [InlineData("mentor-chat")]
    [InlineData("ask-radar")]
    [InlineData("academic-paper-breakdown")]
    [InlineData("learning-section")]
    public void WritingAndJudgementGoesToTheBalancedTier(string feature)
    {
        Assert.Equal(ModelRouter.Tier.Balanced, ModelRouter.TierFor(feature));
    }

    // ── the guard that matters most ──────────────────────────────────────────

    /// <summary>
    /// Every call the codebase actually makes is classified.
    /// </summary>
    /// <remarks>
    /// The failure this exists for is silent. A feature nobody lists still runs - it falls to
    /// Balanced and works - so adding an AI call and forgetting to route it produces no error,
    /// no warning and no symptom beyond a bill and a latency nobody can account for. Reading the
    /// feature names out of the source rather than restating them here is the point: a list
    /// typed twice agrees with itself and not with the code.
    /// </remarks>
    [Fact]
    public void EveryFeatureTheCodeUsesIsRouted()
    {
        var unlisted = FeaturesInSource()
            .Where(feature => !ModelRouter.FastWork.Contains(feature)
                              && !ModelRouter.BalancedWork.Contains(feature)
                              && !ModelRouter.CarefulWork.Contains(feature))
            .OrderBy(f => f)
            .ToList();

        Assert.True(
            unlisted.Count == 0,
            $"These calls are made but not routed, so they silently take the default tier: {string.Join(", ", unlisted)}");
    }

    /// <summary>
    /// And nothing is routed that is not called.
    /// </summary>
    /// <remarks>
    /// The other direction, and the one that caught four invented names when this was written:
    /// "mentor-quiz", "study-plan", "explain-simply" and "lesson-resource-judge" were all
    /// plausible and none of them existed. A route for a feature nobody calls does nothing at
    /// all, and reads as though the call is handled.
    /// </remarks>
    [Fact]
    public void NothingIsRoutedThatIsNeverCalled()
    {
        var real = FeaturesInSource();

        var imaginary = ModelRouter.FastWork
            .Concat(ModelRouter.BalancedWork)
            .Concat(ModelRouter.CarefulWork)
            .Where(feature => !real.Contains(feature))
            .OrderBy(f => f)
            .ToList();

        Assert.True(
            imaginary.Count == 0,
            $"These are routed but never called, so the route does nothing: {string.Join(", ", imaginary)}");
    }

    // ── resolving a model ────────────────────────────────────────────────────

    /// <summary>
    /// With nothing configured, every call goes where it went before.
    /// </summary>
    /// <remarks>
    /// So this can ship dark and be turned on one tier at a time, rather than changing every
    /// call in the product in a single deploy.
    /// </remarks>
    [Fact]
    public void AnUnconfiguredDeploymentBehavesExactlyAsItDid()
    {
        var router = Router(("OpenRouter:Model", "the-model-it-always-used"));

        Assert.Equal("the-model-it-always-used", router.For("document-kind"));
        Assert.Equal("the-model-it-always-used", router.For("project-assessment"));
        Assert.Equal("the-model-it-always-used", router.For("mentor-chat"));
    }

    [Fact]
    public void AConfiguredTierTakesTheCallsInThatTier()
    {
        var router = Router(
            ("OpenRouter:Model", "the-old-one"),
            ("OpenRouter:Models:Fast", "a-quick-model"));

        Assert.Equal("a-quick-model", router.For("document-kind"));

        // And leaves the others where they were, so a tier can be moved on its own.
        Assert.Equal("the-old-one", router.For("mentor-chat"));
        Assert.Equal("the-old-one", router.For("project-assessment"));
    }

    /// <summary>
    /// One route can be pinned without disturbing its tier.
    /// </summary>
    /// <remarks>
    /// Which is how a single misbehaving call gets moved while a measurement on the rest of its
    /// tier is still running.
    /// </remarks>
    [Fact]
    public void APinnedFeatureOutranksItsTier()
    {
        var router = Router(
            ("OpenRouter:Model", "the-old-one"),
            ("OpenRouter:Models:Fast", "a-quick-model"),
            ("OpenRouter:Models:Features:document-kind", "something-else-entirely"));

        Assert.Equal("something-else-entirely", router.For("document-kind"));

        // Its tier-mates are untouched.
        Assert.Equal("a-quick-model", router.For("capture-why"));
    }

    /// <summary>
    /// A feature nobody classified is treated as real work, not as trivia.
    /// </summary>
    /// <remarks>
    /// The cost of being wrong this way is a few cents. The other way is a bad answer in front
    /// of a learner.
    /// </remarks>
    [Fact]
    public void AnUnknownFeatureIsTreatedAsRealWork()
    {
        var router = Router(
            ("OpenRouter:Models:Fast", "a-quick-model"),
            ("OpenRouter:Models:Balanced", "a-capable-model"));

        Assert.Equal(ModelRouter.Tier.Balanced, ModelRouter.TierFor("something-added-next-year"));
        Assert.Equal("a-capable-model", router.For("something-added-next-year"));
    }

    /// <summary>A blank setting is not a configured one.</summary>
    /// <remarks>
    /// An env var set to the empty string is what a half-finished Railway edit looks like, and
    /// sending model="" to the provider fails every call in that tier at once.
    /// </remarks>
    [Fact]
    public void AnEmptySettingFallsThroughRatherThanSendingNothing()
    {
        var router = Router(
            ("OpenRouter:Model", "the-old-one"),
            ("OpenRouter:Models:Fast", ""),
            ("OpenRouter:Models:Features:document-kind", "   "));

        Assert.Equal("the-old-one", router.For("document-kind"));
    }

    // ── reading the source ───────────────────────────────────────────────────

    /// <summary>
    /// Every feature name the production code passes to the engine.
    /// </summary>
    /// <remarks>
    /// Three shapes, because the codebase has three: a literal at the call, a
    /// <c>const string Feature</c> on the service, and the research service's own
    /// <c>CallLlmAsync</c> wrapper. A fourth shape added later would make this under-report,
    /// which is a real limit of reading source with a regular expression - but under-reporting
    /// here fails open on the second test rather than silently passing both.
    /// </remarks>
    private static HashSet<string> FeaturesInSource()
    {
        var patterns = new[]
        {
            new Regex(@"\.(?:GenerateJsonAsync<[^>]*>|GenerateAsync|StreamChatAsync)\s*\(\s*""([^""]+)""", RegexOptions.Singleline),
            new Regex(@"const string Feature\s*=\s*""([^""]+)"""),
            new Regex(@"CallLlmAsync\(\s*""([^""]+)"""),
        };

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in ProductionSources())
        {
            var text = File.ReadAllText(file.FullName);

            foreach (var pattern in patterns)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    found.Add(match.Groups[1].Value);
                }
            }
        }

        Assert.NotEmpty(found);
        return found;
    }

    private static IEnumerable<FileInfo> ProductionSources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return dir!.GetFiles("*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                var path = f.FullName.Replace(Path.DirectorySeparatorChar, '/');
                return !path.Contains("/obj/") && !path.Contains("/bin/") && !path.Contains("/tests/");
            });
    }
}
