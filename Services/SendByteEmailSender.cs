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

    public async Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _log.LogWarning(
                "SendByte is not configured, so a message was not sent. Subject: {Subject}", subject);
            return false;
        }

        // Plain text only. Everything Radar sends today is a short transactional message, and an
        // HTML body would need escaping that nothing currently does — a reset link inside unescaped
        // HTML is a way to break the link, not a way to make it prettier.
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["from"] = From!,
            ["to"] = new[] { to },
            ["subject"] = subject,
            ["text"] = body,
        });

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
            _log.LogError(ex, "Could not reach SendByte to send {Subject}.", subject);
            return false;
        }
    }
}
