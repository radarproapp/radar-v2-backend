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
}
