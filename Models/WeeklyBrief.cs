namespace RadarV2.Models;

public class WeeklyBrief
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public DateTime WeekOf { get; set; }
    public List<Opportunity> TopOpportunities { get; set; } = [];
    public List<ContentItem> TopArticles { get; set; } = [];
    public List<ContentItem> TopResearchPapers { get; set; } = [];
    public List<ContentItem> TopVideos { get; set; } = [];
    public List<ContentItem> TopPodcasts { get; set; } = [];
    public string WeeklyRecommendation { get; set; } = string.Empty;
}
