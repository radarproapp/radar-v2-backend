using System.Net.Http.Headers;
using System.Text.Json;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Transcribes podcast audio via Groq Whisper (whisper-large-v3-turbo).
/// Accepts a public audio URL directly — no download-then-upload required.
/// Used as fallback when Taddy transcript is unavailable.
/// </summary>
public class GroqWhisperClient
{
    // Hard cap on transcriptions per ingestion cycle — each call takes ~10-30s.
    public const int MaxTranscriptionsPerCycle = 3;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration     _config;
    private readonly ILogger<GroqWhisperClient> _log;

    public GroqWhisperClient(IHttpClientFactory httpFactory, IConfiguration config, ILogger<GroqWhisperClient> log)
    {
        _httpFactory = httpFactory;
        _config      = config;
        _log         = log;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["GroqWhisper:ApiKey"]);

    /// <summary>
    /// Transcribes audio the user just recorded in the browser. Unlike the URL overload there is
    /// no public address to hand Groq — the bytes themselves are posted.
    ///
    /// Returns null when the key is absent so callers can say so plainly; a recording that
    /// silently produces nothing is worse than one that is refused up front.
    /// </summary>
    public async Task<string?> TranscribeStreamAsync(
        Stream audio,
        string fileName,
        string? languageHint = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogDebug("GroqWhisper API key not configured — refusing transcription.");
            return null;
        }

        try
        {
            var client = _httpFactory.CreateClient("GroqWhisper");

            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(audio);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", fileName);
            form.Add(new StringContent("whisper-large-v3-turbo"), "model");
            form.Add(new StringContent("json"), "response_format");
            if (!string.IsNullOrWhiteSpace(languageHint))
                form.Add(new StringContent(languageHint), "language");

            using var response = await client.PostAsync("audio/transcriptions", form, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Logged with the body: a 400 from Groq names the problem (unsupported codec,
                // file too large), and swallowing it is how the AI engine stayed broken for days.
                var detail = await response.Content.ReadAsStringAsync(ct);
                _log.LogWarning("GroqWhisper returned {Status} for upload '{File}': {Detail}",
                    response.StatusCode, fileName, detail);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning("GroqWhisper upload transcription failed for '{File}': {Message}", fileName, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Sends a public audio URL to Groq Whisper and returns the full transcript text.
    /// Returns null on failure or if not configured.
    /// </summary>
    public async Task<string?> TranscribeUrlAsync(string audioUrl, string? languageHint = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogDebug("GroqWhisper API key not configured — skipping transcription.");
            return null;
        }

        try
        {
            var client = _httpFactory.CreateClient("GroqWhisper");

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(audioUrl),                           "url");
            form.Add(new StringContent("whisper-large-v3-turbo"),          "model");
            form.Add(new StringContent("json"),                            "response_format");
            if (!string.IsNullOrWhiteSpace(languageHint))
                form.Add(new StringContent(languageHint),                  "language");

            using var response = await client.PostAsync("audio/transcriptions", form, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning("GroqWhisper returned {Status} for {Url}", response.StatusCode, audioUrl);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning("GroqWhisper transcription failed for '{Url}': {Message}", audioUrl, ex.Message);
            return null;
        }
    }
}
