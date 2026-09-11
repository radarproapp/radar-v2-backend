using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IOpportunityService
{
    Task<List<Opportunity>> GetOpportunitiesAsync(UserProfile profile, OpportunityType? filterType = null, int page = 1, int pageSize = 20);
    Task<Opportunity?> GetByIdAsync(string id);
    Task SaveOpportunityAsync(string userId, string opportunityId);
    Task UnsaveOpportunityAsync(string userId, string opportunityId);
    Task<List<Opportunity>> GetSavedOpportunitiesAsync(string userId);
    Task MarkAppliedAsync(string userId, string opportunityId);
}
