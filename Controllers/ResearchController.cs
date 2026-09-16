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
    public async Task<IActionResult> SearchAsync([FromQuery] string q = "", [FromQuery] int yearFrom = 0)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "A search query 'q' is required." });

        var results = await _research.SearchPapersAsync(q.Trim(), yearFrom);
        return Ok(results);
    }
}
