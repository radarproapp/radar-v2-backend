using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IClipsService
{
    Task<List<Clip>> GetDailyClipsAsync(UserProfile profile);
    Task SaveClipAsync(string userId, string clipId);
}
