using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IPlansService
{
    Task<List<SubscriptionPlan>> GetPlansAsync();
    Task<SubscriptionPlan?> GetCurrentPlanAsync(string userId);
}
