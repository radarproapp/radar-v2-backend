using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Helpers;
using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// Assessing a document the learner wrote, as opposed to learning from one they found.
/// </summary>
/// <remarks>
/// Two different things that both start with a file upload, and conflating them is how the
/// existing paths got confusing. <c>POST /api/roadmaps/sections/from-document</c> reads a textbook
/// and builds a path from it. This reads your essay and tells you what is wrong with it.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IUserProfileService _profiles;
    private readonly DocumentAssessmentService _assessments;
    private readonly RadarDatabase _db;

    public DocumentsController(
        IUserProfileService profiles,
        DocumentAssessmentService assessments,
        RadarDatabase db)
    {
        _profiles = profiles;
        _assessments = assessments;
        _db = db;
    }

    /// <summary>
    /// Assesses an uploaded document against criteria for what it is and who wrote it.
    /// </summary>
    /// <remarks>
    /// The kind is detected from the text, never taken from the caller. The existing mentor work
    /// review takes a reviewType from a dropdown, so a CV submitted with the default selected
    /// comes back criticised as an essay.
    /// </remarks>
    /// <summary>
    /// The least that can be assessed.
    /// </summary>
    /// <remarks>
    /// Four hundred characters. Below that there is not enough to judge against a rubric, and an
    /// assessment of it would be confident nonsense about something nobody read - which is the
    /// same failure as a scanned PDF yielding stray characters, arrived at from the other end.
    /// </remarks>
    private const int MinimumPastedLength = 400;

    [HttpPost("assess")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20_000_000)]
    /// <param name="kind">
    /// Optional. What the reader says the document is, which skips detection entirely.
    /// </param>
    /// <param name="text">
    /// The work pasted directly, for somebody who has it in a window rather than a file.
    /// </param>
    public async Task<IActionResult> AssessAsync(
        [FromForm] IFormFile? file,
        [FromForm] DocumentKind? kind = null,
        [FromForm] string? text = null,
        CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        // Pasted or attached. Most writing being assessed is open in another window, and making
        // somebody export it to a file first is a step that exists only because the endpoint
        // wanted one.
        string body;

        if (!string.IsNullOrWhiteSpace(text))
        {
            body = text.Trim();

            if (body.Length < MinimumPastedLength)
            {
                return BadRequest(new
                {
                    error = "There is not enough here to assess. Paste the whole piece of work.",
                });
            }
        }
        else if (file is not null && file.Length > 0)
        {
            if (!DocumentTextExtractor.IsSupported(file.FileName, file.ContentType))
            {
                return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
                {
                    error = "Unsupported format. PDF, Word, Markdown, text, CSV, JSON or HTML.",
                });
            }

            try
            {
                body = await DocumentTextExtractor.ExtractTextAsync(file, ct);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Could not read this document: {ex.Message}" });
            }
        }
        else
        {
            return BadRequest(new { error = "Paste your work, or attach a file." });
        }

        // Scanned PDFs extract a handful of stray characters, and assessing those produces
        // confident nonsense about a document nobody read. There is no OCR here, so say so.
        //
        // Only for files: pasted text short enough to trip this was already refused above, with a
        // message about pasting the whole piece rather than one about scanning.
        if (file is not null && body.Trim().Length < MinimumPastedLength)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                error = "Only a few characters came out of that file. If it is a scan, Radar cannot read it — "
                        + "a text PDF, Word file or pasted text will work.",
            });
        }

        return Ok(await _assessments.AssessAsync(profile, file?.FileName ?? "Pasted text", body, kind, ct));
    }

    /// <summary>Past assessments, newest first, so a revision can be compared with what it fixed.</summary>
    [HttpGet("assessments")]
    public async Task<IActionResult> GetAssessmentsAsync(CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var assessments = await _db.DocumentAssessments
            .Find(a => a.UserId == profile.Id)
            .SortByDescending(a => a.AssessedAt)
            .Limit(25)
            .ToListAsync(ct);

        return Ok(assessments);
    }

    /// <summary>
    /// The criteria a kind of document would be judged against, for this user's persona.
    /// </summary>
    /// <remarks>
    /// Readable before uploading anything. A rubric somebody can see in advance is one they can
    /// write towards, which is more use than the same rubric applied after the fact.
    /// </remarks>
    [HttpGet("rubric/{kind}")]
    public async Task<IActionResult> GetRubricAsync(DocumentKind kind, CancellationToken ct = default)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        var criteria = AssessmentRubric.For(kind, profile.Persona);
        if (criteria.Count == 0)
            return NotFound(new { error = $"No criteria for {kind}." });

        return Ok(new { kind = kind.ToString(), assessedAs = profile.Persona.ToString(), criteria });
    }
}
