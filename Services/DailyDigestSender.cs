using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Turns a digest into a message and sends it.
/// </summary>
/// <remarks>
/// Separated from <see cref="DailyDigestService"/> so that deciding what to say and deciding
/// whether to say it stay apart. The builder can be exercised without a sender configured, which
/// is how the content gets tested at all.
/// </remarks>
public class DailyDigestSender
{
    private readonly RadarDatabase _db;
    private readonly DailyDigestService _digests;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<DailyDigestSender> _log;

    public DailyDigestSender(
        RadarDatabase db,
        DailyDigestService digests,
        IEmailSender email,
        IConfiguration config,
        ILogger<DailyDigestSender> log)
    {
        _db = db;
        _digests = digests;
        _email = email;
        _config = config;
        _log = log;
    }

    public sealed record Result(int Considered, int Sent, int NothingToSay, int Failed);

    /// <summary>
    /// Runs the digest for everybody who asked for it.
    /// </summary>
    /// <remarks>
    /// Opted in only. The preference defaults to off, so this reaches nobody who has not chosen
    /// it, and the result counts how many had nothing worth sending — which is the number worth
    /// watching. A digest that always has something to say about everybody is usually a digest
    /// with a threshold set too low.
    /// </remarks>
    public async Task<Result> RunAsync(CancellationToken ct = default)
    {
        if (!_email.IsConfigured)
        {
            _log.LogWarning("Daily digest skipped: no email sender is configured.");
            return new Result(0, 0, 0, 0);
        }

        var site = (_config["App:PublicUrl"] ?? "https://radarproapp.com").TrimEnd('/');
        var signingKey = _config["Auth:Jwt:Key"] ?? string.Empty;

        var wanted = await _db.Profiles
            .Find(p => p.Notifications.DailyDigest)
            .ToListAsync(ct);

        int sent = 0, quiet = 0, failed = 0;

        foreach (var profile in wanted)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // The address lives on the user, not the profile: one account can hold a student
                // and a graduate profile, and both reach the same inbox.
                var user = await _db.Users.Find(u => u.Id == profile.UserId).FirstOrDefaultAsync(ct);
                if (user is null || string.IsNullOrWhiteSpace(user.Email)) { quiet++; continue; }

                var digest = await _digests.BuildAsync(profile, user.Email, ct);
                if (digest is null) { quiet++; continue; }

                var (subject, text, html) = Compose(digest, profile, site, signingKey);

                if (await _email.SendAsync(user.Email, subject, text, html, ct)) sent++;
                else failed++;
            }
            catch (Exception ex)
            {
                failed++;
                // One profile's failure must not end the run for everybody after it.
                _log.LogWarning(ex, "Daily digest failed for a profile.");
            }
        }

        _log.LogInformation(
            "Daily digest: {Sent} sent, {Quiet} had nothing to say, {Failed} failed, of {Total} opted in.",
            sent, quiet, failed, wanted.Count);

        return new Result(wanted.Count, sent, quiet, failed);
    }

    private static (string Subject, string Text, string Html) Compose(
        DailyDigestService.Digest digest, UserProfile profile, string site, string signingKey)
    {
        var paragraphs = new List<string>();

        // The subject names the thing rather than the product. "Radar daily" tells a reader
        // nothing they can act on; the topic they follow does.
        var subject = digest.New.Count > 0
            ? $"{digest.New[0].Topic}: {digest.New[0].Title}"
            : "A deadline you saved is close";

        if (digest.New.Count > 0)
        {
            paragraphs.Add(digest.New.Count == 1
                ? "One thing arrived overnight in what you follow."
                : $"{digest.New.Count} things arrived overnight in what you follow.");

            foreach (var item in digest.New)
            {
                var why = !string.IsNullOrWhiteSpace(item.WhyItMatters)
                    ? $" {item.WhyItMatters}"
                    : string.Empty;

                paragraphs.Add($"{item.Title} — {item.Source}.{why}");
            }
        }

        // Each of these is a real place they left off, not a nudge invented to fill the message.
        if (digest.Closing.Count > 0)
        {
            foreach (var opportunity in digest.Closing)
            {
                var days = (int)Math.Ceiling((opportunity.Deadline!.Value - DateTime.UtcNow).TotalDays);
                paragraphs.Add($"{opportunity.Title} at {opportunity.Organisation} closes in {days} {(days == 1 ? "day" : "days")}.");
            }
        }

        if (digest.Resume is not null)
        {
            var minutes = (int)Math.Round(digest.ResumePositionSeconds / 60);
            paragraphs.Add($"You stopped {minutes} minutes into {digest.Resume.Title}. It picks up where you left it.");
        }

        if (!string.IsNullOrWhiteSpace(digest.NextLesson))
        {
            paragraphs.Add($"Next on your path: {digest.NextLesson}, in {digest.NextLessonSection}.");
        }

        var token = UnsubscribeToken.Create(profile.Id, UnsubscribeToken.DailyDigest, signingKey);
        var unsubscribe = $"{site}/unsubscribe?token={token}";

        var (html, text) = EmailTemplate.Build(
            heading: $"Today in what you follow, {digest.FirstName}",
            preheader: digest.New.Count > 0
                ? digest.New[0].Title
                : "A deadline you saved is close.",
            paragraphs: paragraphs,
            action: new EmailTemplate.Action_("Open Radar", site),
            footnote: $"You get this because you turned on the daily update. Stop it here: {unsubscribe}",
            publicUrl: site);

        return (subject, text, html);
    }
}
