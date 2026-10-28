using System.Net;
using System.Net.Mail;

namespace RadarV2.Services;

/// <summary>
/// Sending an email.
/// </summary>
/// <remarks>
/// Radar had no way to send one, which is why there was no password reset: a reset flow that
/// cannot deliver a link is a form that silently does nothing.
///
/// SMTP rather than a provider SDK, so any of Resend, Postmark, SendGrid, Mailgun or a plain
/// mailbox works from configuration alone and none of them becomes a dependency in the project
/// file.
/// </remarks>
public interface IEmailSender
{
    bool IsConfigured { get; }

    /// <summary>
    /// Returns false when it could not send, rather than throwing into a request.
    /// </summary>
    /// <param name="text">
    /// The plain-text part. Required, not optional: it is what a screen reader in text mode, a
    /// watch and most spam filters actually read, and a message without one is scored worse for
    /// delivery than one with both.
    /// </param>
    /// <param name="html">The rendered part, when there is one.</param>
    Task<bool> SendAsync(string to, string subject, string text, string? html = null, CancellationToken ct = default);
}

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> log)
    {
        _config = config;
        _log = log;
    }

    private string? Host => _config["Email:SmtpHost"];
    private string? From => _config["Email:From"] ?? _config["SendByte:From"];

    /// <summary>
    /// SendByte documents the username as "apikey" by convention — any value is accepted — and
    /// the password as the API key itself. Falling back to those means switching to the SMTP
    /// relay needs a host and nothing else.
    /// </summary>
    private string? Username => _config["Email:Username"]
        ?? (string.IsNullOrWhiteSpace(_config["SendByte:ApiKey"]) ? null : "apikey");

    private string? Password => _config["Email:Password"] ?? _config["SendByte:ApiKey"];

    /// <summary>
    /// 587 is SendByte's default and uses STARTTLS, which is what EnableSsl does on that port.
    /// 465 is implicit TLS and 2525 is STARTTLS again, for networks that block the other two.
    /// </summary>
    private int Port => _config.GetValue("Email:SmtpPort", 587);

    /// <summary>
    /// A host and a from address at minimum. Credentials are optional — a relay on a private
    /// network may not want them — but without somewhere to send and a sender, nothing works.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);

    public async Task<bool> SendAsync(
        string to, string subject, string text, string? html = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            // Said out loud, because the alternative is a password reset that appears to work and
            // delivers nothing — which is worse than one that is visibly unavailable.
            _log.LogWarning(
                "Email is not configured, so a message to a user was not sent. Subject: {Subject}", subject);
            return false;
        }

        try
        {
            using var client = new SmtpClient(Host, Port) { EnableSsl = true };

            if (!string.IsNullOrWhiteSpace(Username))
                client.Credentials = new NetworkCredential(Username, Password);

            using var message = new MailMessage(From!, to, subject, text);

            // Both parts, with text as the body and html as an alternate view. A client picks
            // whichever it prefers; neither is a fallback for the other.
            if (!string.IsNullOrWhiteSpace(html))
            {
                message.AlternateViews.Add(
                    AlternateView.CreateAlternateViewFromString(html, null, "text/html"));
            }

            await client.SendMailAsync(message, ct);

            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send an email with subject {Subject}.", subject);
            return false;
        }
    }
}
