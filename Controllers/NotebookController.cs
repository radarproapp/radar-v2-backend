using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/notes")]
public class NotebookController : ControllerBase
{
    private readonly INotebookService _notebook;
    private readonly IUserProfileService _profiles;

    public NotebookController(INotebookService notebook, IUserProfileService profiles)
    {
        _notebook = notebook;
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _notebook.GetNotesAsync(profile.Id));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync(string id)
    {
        var note = await _notebook.GetNoteByIdAsync(id);
        return note is null ? NotFound(new { error = "Note not found" }) : Ok(note);
    }

    public sealed class CreateNoteRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateNoteRequest request)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var note = await _notebook.CreateNoteAsync(profile.Id, request.Title, request.Body);
        return Ok(note);
    }

    public sealed class UpdateNoteRequest
    {
        [Required] public string Title { get; set; } = string.Empty;
        [Required] public string Body { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = [];
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(string id, [FromBody] UpdateNoteRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        await _notebook.UpdateNoteAsync(id, request.Title, request.Body, request.Tags);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(string id)
    {
        await _notebook.DeleteNoteAsync(id);
        return NoContent();
    }
}
