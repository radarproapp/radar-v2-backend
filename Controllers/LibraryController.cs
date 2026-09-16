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
        [FromQuery] string? search = null)
    {
        return Ok(await _library.GetAllAsync(
            string.IsNullOrWhiteSpace(category) ? null : category,
            year is > 0 ? year : null,
            string.IsNullOrWhiteSpace(search) ? null : search));
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategoriesAsync() => Ok(await _library.GetCategoriesAsync());
}
