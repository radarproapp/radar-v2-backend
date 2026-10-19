using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Models;
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
    public async Task<IActionResult> GetAllAsync([FromQuery] bool archived = false)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _notebook.GetNotesAsync(profile.Id, archived));
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

    /// <summary>
    /// Every field is optional. The grid edits one property at a time — tick a box, pin, recolour —
    /// and requiring the whole note on each of those would make every small action able to clobber
    /// a concurrent edit.
    /// </summary>
    public sealed class UpdateNoteRequest
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
        public List<string>? Tags { get; set; }
        public bool? IsPinned { get; set; }
        public bool? IsArchived { get; set; }
        public string? Color { get; set; }
        public List<NoteChecklistItem>? Checklist { get; set; }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAsync(string id, [FromBody] UpdateNoteRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();

        var note = await _notebook.UpdateNoteAsync(
            id, request.Title, request.Body, request.Tags,
            request.IsPinned, request.IsArchived, request.Color, request.Checklist);

        // Returns the saved note so the grid can reconcile without a second round trip.
        return note is null ? NotFound(new { error = "Note not found" }) : Ok(note);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAsync(string id)
    {
        await _notebook.DeleteNoteAsync(id);
        return NoContent();
    }
}
