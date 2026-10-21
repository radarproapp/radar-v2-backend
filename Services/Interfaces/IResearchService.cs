using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IResearchService
{
    Task<List<ContentItem>> SearchPapersAsync(string query, int yearFrom = 0, string? paperType = null, int page = 1, int pageSize = 20, UserProfile? profile = null);
    Task<ContentItem?> GetPaperByIdAsync(string id, UserProfile? profile = null);
    Task<List<ContentItem>> GetRelatedPapersAsync(string paperId);
    Task<string> ExplainSimplyAsync(string paperId);
    Task<string> FindResearchGapsAsync(string paperId);
    Task<string> GetCitationAsync(string paperId, string style = "APA");
}
