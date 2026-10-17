using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/research")]
public class ResearchController : ControllerBase
{
    private readonly IResearchService _research;

    public ResearchController(IResearchService research)
    {
        _research = research;
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] string q = "",
        [FromQuery] int yearFrom = 0,
        [FromQuery] string? paperType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "A search query 'q' is required." });

        var results = await _research.SearchPapersAsync(q.Trim(), yearFrom, paperType, Math.Max(1, page), Math.Clamp(pageSize, 5, 50));
        return Ok(results);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetByIdAsync(string id)
    {
        var paper = await _research.GetPaperByIdAsync(id);
        if (paper == null)
            return NotFound(new { error = "Academic paper not found." });

        return Ok(paper);
    }

    [HttpGet("{id}/citation")]
    public async Task<IActionResult> GetCitationAsync(string id, [FromQuery] string style = "APA")
    {
        var citation = await _research.GetCitationAsync(id, style);
        if (string.IsNullOrWhiteSpace(citation))
            return NotFound(new { error = "Paper not found or citation could not be generated." });

        return Ok(new { paperId = id, style, citation });
    }

    [HttpPost("{id}/explain")]
    public async Task<IActionResult> ExplainSimplyAsync(string id)
    {
        var explanation = await _research.ExplainSimplyAsync(id);
        return Ok(new { paperId = id, explanation });
    }

    [HttpPost("{id}/gaps")]
    public async Task<IActionResult> FindResearchGapsAsync(string id)
    {
        var gaps = await _research.FindResearchGapsAsync(id);
        return Ok(new { paperId = id, gaps });
    }

    [HttpGet("{id}/related")]
    public async Task<IActionResult> GetRelatedAsync(string id)
    {
        var related = await _research.GetRelatedPapersAsync(id);
        return Ok(related);
    }
}
