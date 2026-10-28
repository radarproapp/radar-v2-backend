using System.Text;
using System.Text.Json;

namespace RadarV2.Services;

/// <summary>
/// Sends email through SendByte's HTTP API.
/// </summary>
/// <remarks>
/// SendByte offers both SMTP and an HTTP API. The API is the better choice here for a reason that
/// has nothing to do with preference: platforms like Railway routinely block outbound connections
/// on 587, 465 and 2525 to stop their infrastructure being used for spam. An SMTP sender that
/// works locally and times out in production is the worst failure mode available, because it
/// looks like a configuration mistake for as long as anybody is willing to keep checking.
///
/// The API also returns a reason when it refuses — an unverified sending domain, a suppressed
/// recipient, a rate limit — where SMTP gives a numeric code and a disconnect.
///
/// <para>
/// Settings, from SendByte's documentation:
/// <list type="bullet">
/// <item><c>SendByte:ApiKey</c> — an <c>sk_live_</c> key in production, <c>sk_test_</c> to
/// simulate delivery without sending or verifying a domain.</item>
/// <item><c>SendByte:From</c> — the sender. Its domain must be verified for a live key.</item>
/// </list>
/// </para>
/// </remarks>
public class SendByteEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SendByteEmailSender> _log;

    public SendByteEmailSender(
        IConfiguration config,
        IHttpClientFactory httpFactory,
        ILogger<SendByteEmailSender> log)
    {
        _config = config;
        _httpFactory = httpFactory;
        _log = log;
    }

    private string? ApiKey => _config["SendByte:ApiKey"];
    private string? From => _config["SendByte:From"];

    /// <summary>
    /// A key and a sender. Both, because a key with nowhere to send from cannot send.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);

    /// <summary>
    /// Whether this is a sandbox key, which simulates delivery and never reaches an inbox.
    /// </summary>
    /// <remarks>
    /// Surfaced so a reset that silently went nowhere is diagnosable. A test key behaves exactly
    /// like a working one from the application's point of view: 201, an id, no email.
    /// </remarks>
    public bool IsSandbox => ApiKey?.StartsWith("sk_test_", StringComparison.Ordinal) ?? false;

    /// <summary>
    /// What happened the last time Radar tried to send, for /api/version.
    /// </summary>
    /// <remarks>
    /// Added because the answer was otherwise unreachable. The reset endpoint deliberately
    /// returns the same thing whether or not an address has an account, so it cannot report a
    /// send failure; the refusal goes to the host's log dashboard, and on a managed platform
    /// without CLI access that is nowhere.
    ///
    /// So "the key is set and no email arrives" had exactly one diagnostic route: wait, and
    /// guess. This is the same fix /api/ai/status needed for the same reason.
    ///
    /// No recipient address is recorded. The point is whether SendByte accepted it and why not,
    /// which is a status and an error code — a diagnostic endpoint should not also be a list of
    /// who has been emailed.
    /// </remarks>
    public static string? LastSendOutcome { get; private set; }

    public static DateTime? LastSendAt { get; private set; }

    private static void Record(string outcome)
    {
        LastSendOutcome = outcome;
        LastSendAt = DateTime.UtcNow;
    }

    public async Task<bool> SendAsync(
        string to, string subject, string text, string? html = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogWarning(
                "SendByte is not configured, so a message was not sent. Subject: {Subject}", subject);
            return false;
        }

        // Both parts when there is an html one. SendByte takes them together and the reader's
        // client decides; sending html alone would drop the readers who prefer text and cost
        // deliverability with filters that expect a multipart message.
        var fields = new Dictionary<string, object>
        {
            ["from"] = From!,
            ["to"] = new[] { to },
            ["subject"] = subject,
            ["text"] = text,
        };

        if (!string.IsNullOrWhiteSpace(html)) fields["html"] = html;

        var payload = JsonSerializer.Serialize(fields);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/emails")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");

            using var response = await _httpFactory.CreateClient("SendByte").SendAsync(request, ct);

            // 201 on a send, 200 when an idempotency key replays an earlier one.
            if (response.IsSuccessStatusCode)
            {
                Record(IsSandbox
                    ? $"HTTP {(int)response.StatusCode} — accepted by a sandbox key, so not delivered"
                    : $"HTTP {(int)response.StatusCode} — accepted");

                if (IsSandbox)
                {
                    // Said every time rather than once at startup: a sandbox key left in place is
                    // a reset flow that reports success and delivers nothing.
                    _log.LogWarning(
                        "Sent via a SendByte sandbox key — accepted but not delivered. Subject: {Subject}",
                        subject);
                }

                return true;
            }

            var detail = await response.Content.ReadAsStringAsync(ct);

            // Trimmed and kept. "domain_not_verified" is the difference between a key that is
            // wrong and a domain that has not been verified yet, and both look like silence.
            Record($"HTTP {(int)response.StatusCode} — {Trim(detail)}");

            // Kept rather than only logged as a status. "domain_not_verified" and
            // "suppressed_recipient" are both fixable, and neither is guessable from a 403.
            _log.LogError(
                "SendByte refused a message with subject {Subject}: HTTP {Status}. {Detail}",
                subject,
                (int)response.StatusCode,
                detail.Length > 500 ? detail[..500] : detail);

            return false;
        }
        catch (Exception ex)
        {
            Record($"{ex.GetType().Name}: {ex.Message}");
            _log.LogError(ex, "Could not reach SendByte to send {Subject}.", subject);
            return false;
        }
    }

    /// <summary>Enough of the response to identify the error code, and no more.</summary>
    private static string Trim(string detail)
    {
        var flat = detail.ReplaceLineEndings(" ").Trim();
        return flat.Length > 300 ? flat[..300] : flat;
    }
}
