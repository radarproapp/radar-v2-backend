using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IHomeBriefService
{
    /// <summary>
    /// Builds the signed-in home screen from the user's own profile, roadmap, behaviour and the
    /// real content Radar holds. Sections Radar cannot ground come back null or empty rather than
    /// filled with placeholder copy.
    /// </summary>
    Task<HomeBrief> BuildAsync(UserProfile profile, CancellationToken ct = default);
}
