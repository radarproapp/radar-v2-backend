using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IProjectStudioService
{
    Task<List<ProjectTemplate>> GetTemplatesAsync();
    Task<List<StudioProject>> GetUserProjectsAsync(string userId);
    Task<StudioProject> StartProjectAsync(string userId, string templateId);
    Task<StudioProject> CreateProjectAsync(string userId, string title, string description, string category);
    Task CompleteProjectAsync(string userId, string projectId);

    /// <summary>One project owned by this user, including its AI plan when one has been generated.</summary>
    Task<StudioProject?> GetProjectAsync(string userId, string projectId);

    /// <summary>
    /// Builds and persists an execution plan for the project from the user's profile. Returns the
    /// existing plan unchanged if one is already stored, so this is safe to call repeatedly.
    /// </summary>
    Task<StudioProject?> GeneratePlanAsync(string userId, string projectId, UserProfile profile, CancellationToken ct = default);
    Task SetVisibilityAsync(string userId, string projectId, bool isPublic);
    Task<StudioProject?> GetPublicProjectAsync(string projectId);
}
