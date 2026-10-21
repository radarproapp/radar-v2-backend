using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

[ApiController]
[Authorize]
[Route("api/library")]
public class LibraryController : ControllerBase
{
    private readonly ILibraryService _library;

    public LibraryController(ILibraryService library)
    {
        _library = library;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] string? category = null,
        [FromQuery] int? year = null,
        [FromQuery] string? search = null,
        // Spec 8.6's "last five years". Opt-in rather than always-on: the Library also holds
        // foundational work that is older by nature, and hiding it silently would make the
        // collection look thinner than it is without telling the reader anything was withheld.
        [FromQuery] bool recentOnly = false)
    {
        return Ok(await _library.GetAllAsync(
            string.IsNullOrWhiteSpace(category) ? null : category,
            year is > 0 ? year : null,
            string.IsNullOrWhiteSpace(search) ? null : search,
            recentOnly));
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategoriesAsync() => Ok(await _library.GetCategoriesAsync());
}
