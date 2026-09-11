using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ICompareService
{
    Task<PolicyComparison?> GetComparisonAsync(string comparisonId);
    Task<List<PolicyComparison>> GetComparisonsAsync(string userId);
    Task<PolicyComparison> CreateComparisonAsync(string userId, List<string> itemIds);
}
