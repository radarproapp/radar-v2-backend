using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ICompareService
{
    /// <summary>One comparison, by id, belonging to the person asking.</summary>
    Task<PolicyComparison?> GetComparisonAsync(string userId, string comparisonId);
    Task<List<PolicyComparison>> GetComparisonsAsync(string userId);
    Task<PolicyComparison> CreateComparisonAsync(string userId, List<string> itemIds);
}
