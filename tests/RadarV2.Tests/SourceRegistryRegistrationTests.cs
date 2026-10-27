using RadarV2.Services.Ingestion;
namespace RadarV2.Tests;
public class SourceRegistryRegistrationTests
{
    [Fact]
    public void TheStudyFieldSourcesAreActuallyRegistered()
    {
        var ids = SourceRegistry.All.Select(s => s.Id).ToHashSet();
        string[] expected = ["infoq", "stackoverflow-blog", "github-blog", "martin-fowler",
                             "arxiv-cslg", "ieee-spectrum", "new-civil-engineer",
                             "accountancy-age", "pharmacy-times", "techcabal",
                             "openalex-cs", "openalex-pharmacy"];
        var missing = expected.Where(e => !ids.Contains(e)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void EverySourceHasAUniqueId()
    {
        var dupes = SourceRegistry.All.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    /// <summary>
    /// Two ids pointing at one feed is not two sources — it is the same articles, twice.
    ///
    /// Seven feeds were registered twice under slightly different names, and nothing complained:
    /// the registry saw distinct sources, the fetcher pulled both, and url-hash dedupe did not
    /// help because the two copies arrived in the same batch and were compared only against what
    /// was already stored. Measured on staging, four of the top fifty feed items were a duplicate
    /// of another item on the same page.
    /// </summary>
    [Fact]
    public void EveryFeedUrlIsRegisteredOnce()
    {
        var dupes = SourceRegistry.All
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} → {string.Join(", ", g.Select(s => s.Id))}")
            .ToList();

        Assert.Empty(dupes);
    }
}
