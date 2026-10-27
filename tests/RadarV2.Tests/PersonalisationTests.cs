using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// Personalisation is a hard requirement: the feed does not populate until it is answered.
///
/// Whether someone has personalised is derived rather than stored. A flag would need a migration
/// and would lie about anyone who completed the older onboarding, which never asked for goals —
/// they would read as personalised with box 2 empty.
/// </summary>
public class PersonalisationTests
{
    [Fact]
    public void ANewProfileIsNotPersonalised()
    {
        Assert.False(new UserProfile().IsPersonalised);
    }

    // The case a stored flag would get wrong: the older onboarding collected interests and a
    // single goal string, never the goals list.
    [Fact]
    public void InterestsAloneAreNotEnough()
    {
        var profile = new UserProfile
        {
            Interests = ["Computer Science"],
            PrimaryGoal = "Computer Science",
        };

        Assert.False(profile.IsPersonalised);
    }

    [Fact]
    public void GoalsAloneAreNotEnough()
    {
        Assert.False(new UserProfile { Goals = ["Find a job"] }.IsPersonalised);
    }

    [Fact]
    public void OneOfEachIsEnough()
    {
        var profile = new UserProfile
        {
            Interests = ["Software Engineering"],
            Goals = ["Find a job"],
        };

        Assert.True(profile.IsPersonalised);
    }

    // Asked for, never required. Someone who wants nothing applied for should still get a feed.
    [Fact]
    public void OpportunityPreferencesAreOptional()
    {
        var profile = new UserProfile
        {
            Interests = ["Nursing"],
            Goals = ["Improve academically"],
            OpportunityPreferences = [],
        };

        Assert.True(profile.IsPersonalised);
    }

    // Free text arrives from the three boxes; whitespace is not an answer.
    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void BlankAnswersDoNotCount(string blank)
    {
        var profile = new UserProfile
        {
            Interests = ["Law"],
            Goals = [blank],
        };

        Assert.False(profile.IsPersonalised);
    }

    /// <summary>
    /// Every way of asking for a feed has to apply the rule, not just the first one written.
    ///
    /// The gap this closes was real and mine: the rule went into GetFeedAsync, while
    /// GetFeedPageAsync — the method the feed page actually calls — only named
    /// NotPersonalisedYet in its reason field and otherwise ran the full query against an empty
    /// profile. The client showed the three questions above a complete page of unranked content,
    /// which is the exact thing the rule exists to prevent.
    ///
    /// Read from source because the service needs a live Mongo connection and there is no
    /// fixture for one. A structural test is worth less than a behavioural one; it is worth more
    /// than the nothing that was here when the second method was missed.
    /// </summary>
    [Fact]
    public void EveryFeedEntryPointChecksPersonalisationBeforeQuerying()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName, "Services", "MongoIntelligenceFeedService.cs"));

        // Method bodies, split on the signature line of each public member.
        var methods = source.Split("    public ")
            .Where(chunk => chunk.StartsWith("async Task") && chunk.Contains("GetFeed"))
            .ToList();

        Assert.Equal(2, methods.Count);

        foreach (var method in methods)
        {
            var opening = method[..Math.Min(120, method.Length)];
            var gate = method.IndexOf("IsPersonalised", StringComparison.Ordinal);
            var firstQuery = method.IndexOf(".Find(", StringComparison.Ordinal);

            Assert.True(gate >= 0, $"Never consults IsPersonalised: {opening}");
            Assert.True(
                firstQuery < 0 || gate < firstQuery,
                $"Queries content before checking IsPersonalised: {opening}");
        }
    }

    /// <summary>
    /// Walks up from the test binary to the repository root, which is wherever the project file
    /// sits, rather than hardcoding a path that breaks when CI checks out elsewhere.
    /// </summary>
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
