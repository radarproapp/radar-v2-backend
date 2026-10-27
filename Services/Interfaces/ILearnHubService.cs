using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface ILearnHubService
{
    Task<LearnHubPayload> GetLearnHubAsync(string userId, string? subInterest = null, string? stage = null);
    Task CompleteResourceAsync(string userId, string resourceId);

    /// <summary>Marks the hands-on exercise on a resource as done, or undone.</summary>
    Task CompleteExerciseAsync(string userId, string resourceId);
    Task ToggleSaveResourceAsync(string userId, string resourceId);
    Task RecordNodeActivityAsync(string userId, NodeActivityRequest request);
}
