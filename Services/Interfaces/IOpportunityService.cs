using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IOpportunityService
{
    Task<List<Opportunity>> GetOpportunitiesAsync(
        UserProfile profile,
        OpportunityType? filterType = null,
        int page = 1,
        int pageSize = 20);

    Task<List<Opportunity>> GetOpportunitiesAsync(
        UserProfile profile,
        OpportunityType? filterType = null,
        string? region = null,
        string? goal = null,
        string? urgency = null,
        string? matchTier = null,
        int page = 1,
        int pageSize = 20);

    Task<DailyOpportunityFeed> GetDailyFeedAsync(UserProfile profile);

    Task<Opportunity?> GetByIdAsync(string id, UserProfile? profile = null);

    Task SaveOpportunityAsync(string userId, string opportunityId);

    Task UnsaveOpportunityAsync(string userId, string opportunityId);

    Task<List<Opportunity>> GetSavedOpportunitiesAsync(string userId);

    Task MarkAppliedAsync(string userId, string opportunityId);

    Task UpdateStatusAsync(string userId, string opportunityId, OpportunityTrackingStatus status, string? notes = null);

    Task ToggleReminderAsync(string userId, string opportunityId, bool reminderSet);

    Task<List<Opportunity>> GetTrackedPipelineAsync(string userId);
}
