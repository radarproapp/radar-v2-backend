using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IUserLearningRecordService
{
    /// <summary>
    /// Rebuilds the user's consolidated learning record from profile, behavioural signals and the
    /// active roadmap. Derived on read, so it can never drift from its sources.
    /// </summary>
    Task<UserLearningRecord> BuildAsync(UserProfile profile, CancellationToken ct = default);
}
