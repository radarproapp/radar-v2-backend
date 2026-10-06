using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RadarV2.Controllers;

/// <summary>
/// Diagnostic endpoint for the AI layer. Reports whether an OpenRouter key is configured and runs
/// one tiny live completion, returning the provider's raw status — so a bad key, model name or
/// exhausted credits is visible without reading server logs. Never returns the key itself.
/// </summary>
[ApiController]
[Authorize]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;

    public HealthController(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    [HttpGet("ai")]
    public async Task<IActionResult> AiAsync(CancellationToken ct)
    {
        var key = _config["OpenRouter:ApiKey"] ?? string.Empty;
        var model = _config["OpenRouter:Model"] ?? "deepseek/deepseek-chat";
        var baseUrl = (_config["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1").TrimEnd('/') + "/";
        var configured = !string.IsNullOrWhiteSpace(key);

        object? probe = null;
        if (configured)
        {
            var client = _httpFactory.CreateClient("OpenRouter");
            try
            {
                var payload = new
                {
                    model,
                    messages = new[] { new { role = "user", content = "Reply with the single word: ok" } },
                    max_tokens = 5,
                };
                var response = await client.PostAsJsonAsync("chat/completions", payload, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                probe = new
                {
                    status = (int)response.StatusCode,
                    ok = response.IsSuccessStatusCode,
                    // Truncated: enough to read an error like "invalid api key" or "insufficient credits".
                    snippet = body.Length > 500 ? body[..500] : body,
                };
            }
            catch (Exception ex)
            {
                probe = new { error = ex.GetType().Name, message = ex.Message };
            }
        }

        return Ok(new { configured, keyLength = key.Length, model, baseUrl, probe });
    }
}
