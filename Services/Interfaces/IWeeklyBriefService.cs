using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IWeeklyBriefService
{
    Task<WeeklyBrief?> GetLatestBriefAsync(string userId);
    Task<WeeklyBrief> GenerateBriefAsync(string userId, UserProfile profile);
}
