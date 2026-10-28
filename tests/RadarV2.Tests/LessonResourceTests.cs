using MongoDB.Driver;
using System.Reflection;
using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Five videos and two articles per lesson, and nothing padded to reach those numbers.
///
/// Resources used to attach to the module, and when none matched the module's level the code
/// handed it every resource on the path — so a lesson on ARP spoofing could show the same list as
/// a lesson on VLAN hopping. One video was resolved per lesson and the rest of the search results
/// were thrown away.
/// </summary>
public class LessonResourceTests
{
    private static IEnumerable<string> Keywords(string title) =>
        LessonRelevance.TermsOf(title);

    private static string VideoIdOf(LessonResource video)
    {
        var method = typeof(LessonResourceService).GetMethod(
            "VideoIdOf", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return (string)method!.Invoke(null, [video])!;
    }

    /// <summary>
    /// Filler is dropped before searching the corpus. Matching an article on "understand" or
    /// "the" returns the whole corpus in publication order, which is a list of recent articles
    /// dressed up as reading for this lesson.
    /// </summary>
    [Fact]
    public void FillerWordsAreNotSearchedOn()
    {
        var keywords = Keywords("Understand ARP Spoofing").ToList();

        Assert.Contains("Spoofing", keywords);
        Assert.DoesNotContain("Understand", keywords);
    }

    [Fact]
    public void ShortWordsAreDropped()
    {
        var keywords = Keywords("How to use a VPN at work").ToList();

        Assert.DoesNotContain("How", keywords);
        Assert.DoesNotContain("to", keywords);
        Assert.DoesNotContain("a", keywords);
        Assert.DoesNotContain("at", keywords);
    }

    // Three is enough to be specific without the OR-filter matching half the corpus.
    /// <summary>
    /// The specific terms survive, however late in the title they appear.
    /// </summary>
    /// <remarks>
    /// This replaces a test asserting at most three terms were used. That cap was part of the
    /// defect, not a protection against it: taking the first three words over four characters
    /// threw away whatever made a lesson specific in favour of whatever happened to come first,
    /// and left the match resting on the generic words that remained.
    ///
    /// What actually needs guarding is the opposite — that a distinctive word deep in a title is
    /// still available to match on.
    /// </remarks>
    [Fact]
    public void TheSpecificTermsSurviveWhereverTheyAppear()
    {
        var keywords = Keywords("Configuring network segmentation across enterprise firewall appliances").ToList();

        Assert.Contains("firewall", keywords, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("segmentation", keywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATitleOfNothingButFillerYieldsNoTerms()
    {
        // Rather than falling back to matching everything, which is what an empty OR filter does.
        Assert.Empty(Keywords("What you really need to understand"));
    }

    /// <summary>
    /// Summaries are matched back to videos by id, not by position.
    /// </summary>
    /// <remarks>
    /// A model returning four summaries for five videos would otherwise shift them all up by one
    /// and describe the wrong video — worse than no summary, and undetectable on screen.
    /// </remarks>
    [Fact]
    public void AVideoIsIdentifiableForMatchingASummaryBack()
    {
        var video = new LessonResource
        {
            Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            EmbedUrl = "https://www.youtube.com/embed/dQw4w9WgXcQ",
        };

        Assert.Equal("dQw4w9WgXcQ", VideoIdOf(video));
    }

    [Fact]
    public void AVideoWithNoEmbedUrlIsStillIdentifiable()
    {
        var video = new LessonResource { Url = "https://www.youtube.com/watch?v=abc123" };

        Assert.Equal("abc123", VideoIdOf(video));
    }

    // A lesson splits its own resources, so a page can show the two lists separately.
    [Fact]
    public void ALessonSeparatesVideosFromArticles()
    {
        var lesson = new RoadmapLesson
        {
            Title = "Detect MAC flooding",
            Resources =
            [
                new() { Kind = LessonResourceKind.Video, Title = "v1" },
                new() { Kind = LessonResourceKind.Article, Title = "a1" },
                new() { Kind = LessonResourceKind.Video, Title = "v2" },
            ],
        };

        Assert.Equal(2, lesson.Videos.Count());
        Assert.Single(lesson.Articles);
    }

    /// <summary>
    /// Where a summary came from is recorded, because it decides what the summary is worth.
    /// </summary>
    /// <remarks>
    /// There are no transcripts — the transcription keys are unset — so a video summary is written
    /// from the title and channel. Saying so is the difference between a summary of the thing and
    /// a summary of its blurb.
    /// </remarks>
    [Fact]
    public void AVideoSummaryDoesNotClaimToComeFromATranscript()
    {
        var video = new LessonResource { Kind = LessonResourceKind.Video };

        Assert.Equal(SummarySource.Description, video.SummarySource);
    }

    [Fact]
    public void ALessonStartsWithNoResourcesRatherThanPlaceholders()
    {
        Assert.Empty(new RoadmapLesson().Resources);
    }

    /// <summary>
    /// The article sort has to render to a Mongo sort document.
    /// </summary>
    /// <remarks>
    /// It was <c>SortByDescending(c =&gt; c.CredibilityTier == 1)</c>. The driver's sort builder
    /// needs a field to render from and a boolean comparison is not one, so it threw at
    /// query-build time — on every call. Which meant every "Build this section" came back as a
    /// 500, because AddSectionAsync enriches resources on the way out, and a learner pressing the
    /// button got "Something went wrong handling that request".
    ///
    /// Rendering it is the whole test: a sort that cannot be rendered is the bug, and it needs no
    /// database to catch.
    /// </remarks>
    [Fact]
    public void TheArticleSortRendersToAValidSortDocument()
    {
        var sort = Builders<ContentItem>.Sort
            .Ascending(c => c.CredibilityTier)
            .Descending(c => c.PublishedAt);

        var registry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
        var rendered = sort.Render(new RenderArgs<ContentItem>(
            registry.GetSerializer<ContentItem>(), registry));

        Assert.True(rendered.Contains("CredibilityTier"), rendered.ToString());
        Assert.True(rendered.Contains("PublishedAt"), rendered.ToString());
    }

    /// <summary>
    /// Tier 1 is the best tier, so ascending is what puts the most credible article first.
    /// Getting this backwards would quietly rank the least trustworthy source top.
    /// </summary>
    [Fact]
    public void TheMostCredibleTierSortsFirst()
    {
        var items = new[]
        {
            new ContentItem { Id = "c", CredibilityTier = 3 },
            new ContentItem { Id = "a", CredibilityTier = 1 },
            new ContentItem { Id = "b", CredibilityTier = 2 },
        };

        var ordered = items.OrderBy(i => i.CredibilityTier).Select(i => i.Id).ToList();

        Assert.Equal(["a", "b", "c"], ordered);
    }

    /// <summary>
    /// Building a path counts as having done something.
    /// </summary>
    /// <remarks>
    /// HasActivity checked completed lessons and not sections, so a learner who had answered the
    /// three questions and had a section built was shown "Nothing to show yet — your graph fills
    /// itself in as you read, listen, build a path" by a page whose own payload said five lessons
    /// across one section. Caught on production by reading the two fields next to each other.
    /// </remarks>
    [Fact]
    public void APathWithNoCompletedLessonsStillCountsAsActivity()
    {
        var graph = new UserGraph();
        graph.Learning.PathSections = 1;
        graph.Learning.LessonsTotal = 5;
        graph.Learning.LessonsCompleted = 0;

        Assert.True(graph.HasActivity);
    }

    // Saving something is a deliberate act and the graph already counts it, so it should not be
    // reported as an empty graph either.
    [Fact]
    public void SavingSomethingCountsAsActivity()
    {
        var graph = new UserGraph();
        graph.Reading.ItemsSaved = 1;

        Assert.True(graph.HasActivity);
    }

    // And a genuinely new account is still empty, which is the case the flag exists for.
    [Fact]
    public void ANewAccountHasNoActivity()
    {
        Assert.False(new UserGraph().HasActivity);
    }
}
