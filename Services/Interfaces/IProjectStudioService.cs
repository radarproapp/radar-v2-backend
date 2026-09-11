using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IProjectStudioService
{
    Task<List<ProjectTemplate>> GetTemplatesAsync();
    Task<List<StudioProject>> GetUserProjectsAsync(string userId);
    Task<StudioProject> StartProjectAsync(string userId, string templateId);
    Task CompleteProjectAsync(string userId, string projectId);
}
