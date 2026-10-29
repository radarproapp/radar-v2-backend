using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// Where opportunities come from.
/// </summary>
/// <remarks>
/// Radar is for students, graduates and professionals in Africa, and every source here was
/// global, pan-African or remote. So a graduate in Lagos looking for work in Lagos was offered
/// scholarships and remote programming roles — the opportunities most users are actually applying
/// for came from nowhere.
/// </remarks>
public class OpportunitySourceTests
{
    /// <summary>
    /// Somewhere in the list posts jobs in Nigeria.
    /// </summary>
    /// <remarks>
    /// The whole product is aimed at a market whose ordinary job postings it did not carry. This
    /// is a weaker guarantee than checking the feed still returns entries — nothing here makes a
    /// network call — but the gap it guards against was a list nobody read for months.
    /// </remarks>
    [Fact]
    public void SomethingPostsJobsInNigeria()
    {
        var nigerianJobs = OpportunitySourceRegistry.All
            .Where(feed => feed.RegionTag.Equals("Nigeria", StringComparison.OrdinalIgnoreCase))
            .Where(feed => feed.DefaultType is OpportunityType.Job or OpportunityType.RemoteJob)
            .ToList();

        Assert.NotEmpty(nigerianJobs);
    }

    /// <summary>Every feed is listed once, and every one has somewhere to fetch from.</summary>
    [Fact]
    public void EveryFeedIsDistinctAndAddressed()
    {
        var urls = OpportunitySourceRegistry.All.Select(f => f.Url).ToList();

        Assert.Equal(urls.Count, urls.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(OpportunitySourceRegistry.All, feed =>
        {
            Assert.False(string.IsNullOrWhiteSpace(feed.Name));
            Assert.StartsWith("https://", feed.Url);
        });
    }

    /// <summary>
    /// A source that was checked and refused stays refused.
    /// </summary>
    /// <remarks>
    /// Jobberman is the one everybody asks for, and it publishes no job feed: the only feed it
    /// advertises is its blog, and its robots.txt disallows the listing paths. Reaching its jobs
    /// would mean an agreement with them rather than a crawler, so the reason is written down
    /// where somebody about to add it will read it.
    /// </remarks>
    [Fact]
    public void TheSourcesThatWereRefusedAreNotQuietlyAdded()
    {
        var registered = string.Join(" ", OpportunitySourceRegistry.All.Select(f => f.Url))
            .ToLowerInvariant();

        Assert.DoesNotContain("jobberman", registered);
        Assert.DoesNotContain("myjobmag", registered);
    }
}
