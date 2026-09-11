namespace RadarV2.Models;

public class FeedSource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public FeedSourceType SourceType { get; set; } = FeedSourceType.Rss;
    public ContentLayer Layer { get; set; }
    public ContentType DefaultContentType { get; set; } = ContentType.Article;
    public int Tier { get; set; }
    public List<string> Topics { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
}

public enum FeedSourceType
{
    Rss,
    Atom,
    OpenAlexApi,
    MediastackApi,
    PodcastIndexApi
}
