using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadarV2.Helpers;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Controllers;

/// <summary>
/// Ask Radar �?" general-purpose chat grounded in the user's profile. The interactive chat page
/// uses the SSE stream endpoint; one-shot callers (e.g. Project Studio's "Ask Radar for tips",
/// which just wants a finished string to display, not a typing effect) use the plain endpoint.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ask")]
public class AskRadarController : ControllerBase
{
    private readonly IAskRadarService _ask;
    private readonly IUserProfileService _profiles;
    private readonly IHttpClientFactory _httpFactory;

    public AskRadarController(IAskRadarService ask, IUserProfileService profiles, IHttpClientFactory httpFactory)
    {
        _ask = ask;
        _profiles = profiles;
        _httpFactory = httpFactory;
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

        var response = await _ask.SendMessageAsync(profile.Id, request.Message, request.History);
        await _ask.AppendAsync(profile.Id,
        [
            new ChatMessage { Role = "user", Content = request.Message },
            new ChatMessage { Role = "assistant", Content = response.Content },
        ]);
        return Ok(response);
    }

    /// <summary>The user's saved Ask Radar conversation, oldest first.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistoryAsync([FromQuery] int limit = 60)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        return Ok(await _ask.GetHistoryAsync(profile.Id, Math.Clamp(limit, 1, 200)));
    }

    /// <summary>Starts a new conversation by clearing the saved history.</summary>
    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistoryAsync()
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        await _ask.ClearHistoryAsync(profile.Id);
        return NoContent();
    }

    [HttpPost("chat/stream")]
    public async Task ChatStreamAsync([FromBody] ChatRequest request, CancellationToken ct)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Accumulate the streamed reply so it can be saved once streaming finishes; the client
        // still receives each chunk live through the tee.
        var reply = new StringBuilder();

        async IAsyncEnumerable<string> Tapped(CancellationToken token)
        {
            await foreach (var chunk in _ask.StreamMessageAsync(profile.Id, request.Message, request.History, token))
            {
                reply.Append(chunk);
                yield return chunk;
            }
        }

        try
        {
            await SseWriter.WriteChatStreamAsync(Response, Tapped(ct), ct);
        }
        finally
        {
            if (reply.Length > 0)
                await _ask.AppendAsync(profile.Id,
                [
                    new ChatMessage { Role = "user", Content = request.Message },
                    new ChatMessage { Role = "assistant", Content = PlainText.Clean(reply.ToString()) },
                ]);
        }
    }

    public sealed class LinkRequest
    {
        [Required, MinLength(4)] public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Fetch a URL server-side, extract its readable text and summarise it against the user's
    /// profile. Doing the fetch here (rather than trusting the client to paste the page) is what
    /// makes "Summarize link" actually summarise the link.
    /// </summary>
    [HttpPost("summarize-link")]
    public async Task<IActionResult> SummarizeLinkAsync([FromBody] LinkRequest request)
    {
        if (!ModelState.IsValid) return ValidationProblem();
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return BadRequest(new { error = "Enter a valid http(s) link." });

        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });

        string text;
        try
        {
            var client = _httpFactory.CreateClient("Ingestion");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var html = await client.GetStringAsync(uri, cts.Token);
            text = StripHtml(html);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Couldn't fetch that page ({ex.GetType().Name})." });
        }

        if (string.IsNullOrWhiteSpace(text)) return BadRequest(new { error = "That page had no readable text." });
        text = Truncate(text, 8000);

        var reply = await _ask.SendMessageAsync(profile.Id,
            $"Summarize this webpage for me in a few tight paragraphs: what happened, why it matters to my goals, and my next move. Infer two or three topic tags.\n\nURL: {uri}\n\n{text}", []);
        await _ask.AppendAsync(profile.Id,
        [
            new ChatMessage { Role = "user", Content = $"Summarize link: {request.Url}" },
            new ChatMessage { Role = "assistant", Content = reply.Content },
        ]);
        return Ok(new { summary = reply.Content });
    }

    /// <summary>
    /// Summarise an uploaded text document. PDFs do not have a reliable text extractor here, so
    /// they are rejected with a clear message rather than silently summarising nothing.
    /// </summary>
    [HttpPost("summarize-file")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> SummarizeFileAsync(IFormFile? file)
    {
        var profile = await _profiles.GetCurrentUserAsync();
        if (profile is null) return NotFound(new { error = "Profile not found" });
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var textExtensions = new[] { ".txt", ".md", ".markdown", ".csv", ".json", ".html", ".htm", ".rtf", ".xml", ".log" };
        var isText = textExtensions.Contains(ext) || (file.ContentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!isText)
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
            {
                error = "Only text documents are supported right now (.txt, .md, .csv, .json, .html, .xml). PDFs aren't supported yet — copy the text in for now.",
            });

        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text)) return BadRequest(new { error = "That file was empty." });
        text = Truncate(text, 12000);

        var reply = await _ask.SendMessageAsync(profile.Id,
            $"Summarize this document for me. Key points, why they matter to my goals, and my next move.\n\nFile: {file.FileName}\n\n{text}", []);
        await _ask.AppendAsync(profile.Id,
        [
            new ChatMessage { Role = "user", Content = $"Summarize file: {file.FileName}" },
            new ChatMessage { Role = "assistant", Content = reply.Content },
        ]);
        return Ok(new { summary = reply.Content });
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string StripHtml(string html)
    {
        html = Regex.Replace(html, "<script[^>]*>.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style[^>]*>.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        return Regex.Replace(html, "\\s+", " ").Trim();
    }
}
