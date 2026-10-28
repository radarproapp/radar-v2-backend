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

    /// <summary>Returns false when it could not send, rather than throwing into a request.</summary>
    Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default);
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
    private string? From => _config["Email:From"];
    private string? Username => _config["Email:Username"];
    private string? Password => _config["Email:Password"];
    private int Port => _config.GetValue("Email:SmtpPort", 587);

    /// <summary>
    /// A host and a from address at minimum. Credentials are optional — a relay on a private
    /// network may not want them — but without somewhere to send and a sender, nothing works.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);

    public async Task<bool> SendAsync(string to, string subject, string body, CancellationToken ct = default)
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

            using var message = new MailMessage(From!, to, subject, body);
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
