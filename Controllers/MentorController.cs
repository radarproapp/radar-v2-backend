using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// AI Learning Mentor — chat, quiz, study plan, work review. Chat is a plain request/response
/// call for now: ILearningMentorService also exposes StreamMessageAsync (SSE-style), but wiring
/// real-time streaming is an explicitly separate, later phase (REACT_MIGRATION.md §5 Phase 3
/// step 5, grouped with Ask Radar's streaming) — not part of this bulk port.
/// </summary>
[ApiController]
[Authorize]
[Route("api/mentor")]
public class MentorController : ControllerBase
{
    private readonly ILearningMentorService _mentor;
    private readonly IUserProfileService _profiles;

    public MentorController(ILearningMentorService mentor, IUserProfileService profiles)
    {
        _mentor = mentor;
        _profiles = profiles;
    }

    public sealed class ChatRequest
    {
        [Required, MinLength(1)] public string Message { get; set; } = string.Empty;
        public List<ChatMessage> History { get; set; } = [];
    }

    [HttpPost("chat")]
    public async Task<IActionResult> ChatAsync([FromBody] ChatRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var response = await _mentor.SendMessageAsync(profile.Id, request.Message, request.History);
        return Ok(response);
    }

    public sealed class GenerateQuizRequest
    {
        [Required, MinLength(1)] public string Topic { get; set; } = string.Empty;
        public int QuestionCount { get; set; } = 5;
    }

    [HttpPost("quiz")]
    public async Task<IActionResult> GenerateQuizAsync([FromBody] GenerateQuizRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var quiz = await _mentor.GenerateQuizAsync(profile.Id, request.Topic, Math.Clamp(request.QuestionCount, 1, 20));
        return Ok(quiz);
    }

    public sealed class SubmitAnswerRequest
    {
        [Required] public int QuestionIndex { get; set; }
        [Required] public int AnswerIndex { get; set; }
    }

    [HttpPost("quiz/{quizId}/answer")]
    public async Task<IActionResult> SubmitAnswerAsync(string quizId, [FromBody] SubmitAnswerRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var quiz = await _mentor.SubmitAnswerAsync(quizId, request.QuestionIndex, request.AnswerIndex);
        return Ok(quiz);
    }

    public sealed class CreateStudyPlanRequest
    {
        [Required, MinLength(1)] public string Topic { get; set; } = string.Empty;
        [Required, MinLength(1)] public string Goal { get; set; } = string.Empty;
    }

    [HttpPost("study-plan")]
    public async Task<IActionResult> CreateStudyPlanAsync([FromBody] CreateStudyPlanRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var plan = await _mentor.CreateStudyPlanAsync(profile.Id, request.Topic, request.Goal);
        return Ok(plan);
    }

    [HttpGet("study-plan/active")]
    public async Task<IActionResult> GetActiveStudyPlanAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var plan = await _mentor.GetActiveStudyPlanAsync(profile.Id);
        return plan is null ? NoContent() : Ok(plan);
    }

    public sealed class ReviewWorkRequest
    {
        [Required, MinLength(1)] public string WorkTitle { get; set; } = string.Empty;
        [Required, MinLength(1)] public string WorkContent { get; set; } = string.Empty;
        [Required, MinLength(1)] public string ReviewType { get; set; } = string.Empty;
    }

    [HttpPost("review")]
    public async Task<IActionResult> ReviewWorkAsync([FromBody] ReviewWorkRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var review = await _mentor.ReviewWorkAsync(profile.Id, request.WorkTitle, request.WorkContent, request.ReviewType);
        return Ok(review);
    }
}
