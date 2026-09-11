using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ICaptureService
{
    Task<CapturedItem> CaptureAsync(string userId, CaptureMode mode, string input);
    Task<List<CapturedItem>> GetRecentCapturesAsync(string userId);
}
