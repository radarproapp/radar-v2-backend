using RadarV2.Models;

namespace RadarV2.Services.Interfaces;

public interface IProjectStudioService
{
    /// <summary>
    /// Projects worth building next, taken from the learner's own path.
    /// </summary>
    /// <remarks>
    /// This replaced a list of six templates served to everybody: Product Case Study, Marketing
    /// Campaign, Research Proposal, Literature Review, Business Model Canvas, Portfolio Project.
    /// A learner studying mathematics was offered a Business Model Canvas, which is not a
    /// suggestion - it is a catalogue with nobody's name on it.
    ///
    /// The real ones already existed. StageProjectCatalog builds a project per domain per stage,
    /// and the personaliser attaches one to every module of a learner's path - so by the time
    /// somebody opens the studio, Radar already knows what they should build and why it proves
    /// something. The studio was the only place not reading it.
    /// </remarks>
    Task<List<StageProject>> GetSuggestedProjectsAsync(string userId);
    Task<List<StudioProject>> GetUserProjectsAsync(string userId);
    /// <summary>
    /// Starts one of the suggested projects, or returns the one already under way.
    /// </summary>
    Task<StudioProject?> StartSuggestedAsync(string userId, string projectId);

    /// <summary>
    /// Records an attempt at a project.
    /// </summary>
    /// <remarks>
    /// Appended rather than replacing the last one. A project under the bar is meant to be
    /// revised and handed in again, and the earlier attempts are the evidence that it was.
    /// </remarks>
    Task<StudioProject?> SubmitAsync(string userId, string projectId, ProjectSubmission submission);
    Task<StudioProject> CreateProjectAsync(string userId, string title, string description, string category);
    /// <summary>
    /// Accepts a project when its latest attempt scored at or above the bar. Returns null when
    /// accepted, or the reason it was not.
    /// </summary>
    Task<string?> CompleteProjectAsync(string userId, string projectId);

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
