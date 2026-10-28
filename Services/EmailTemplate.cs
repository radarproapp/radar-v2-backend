using System.Net;
using System.Text;

namespace RadarV2.Services;

/// <summary>
/// Radar's email layout.
/// </summary>
/// <remarks>
/// Email is not the web. The rules that shape this, each of which costs something if ignored:
///
/// <list type="bullet">
/// <item><b>Tables, not flex or grid.</b> Outlook renders through Word's HTML engine, which has
/// no support for either. A flexbox layout collapses to a single stacked column there.</item>
/// <item><b>Inline styles.</b> Gmail strips a <c>&lt;style&gt;</c> block in forwarded messages and
/// in some clipped views, so anything load-bearing has to be on the element.</item>
/// <item><b>System fonts.</b> A webfont is ignored by Outlook and blocked by several privacy
/// clients, and the fallback is what most people will actually see — so the fallback is the
/// design.</item>
/// <item><b>The message must work with images off.</b> Most clients block remote images by
/// default on a first message from a new sender. So the logo carries no information: everything
/// the reader needs is text, and the button is a styled table cell rather than an image.</item>
/// <item><b>A preheader.</b> The inbox preview line otherwise shows whatever text comes first,
/// which without one is usually "View this email in your browser" or the alt text of the
/// logo.</item>
/// </list>
///
/// <para>
/// The header band is <c>#191a2e</c>, which is the logo's own background rather than the app's
/// <c>--navy</c> of <c>#12161d</c>. They are close enough to look like a mistake and far enough
/// apart to show a seam where the image meets the band.
/// </para>
/// </remarks>
public static class EmailTemplate
{
    /// <summary>The logo's own navy. Not the app's --navy, which is a few points darker.</summary>
    private const string Navy = "#191a2e";

    /// <summary>--cyan-deep. Chosen over --cyan-bright because white sits on it legibly.</summary>
    private const string Action = "#046b70";

    private const string Ink = "#12161d";
    private const string Body = "#434a54";
    private const string Muted = "#8b949e";
    private const string Line = "#e4e8ec";

    /// <summary>
    /// Georgia is absent on purpose. This is a product message, not a newsletter, and the stack
    /// below is what renders natively on every client without a download.
    /// </summary>
    private const string Font =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif";

    /// <summary>One call to action. More than one is a message that has not decided what it wants.</summary>
    public sealed record Action_(string Label, string Url);

    /// <summary>
    /// Builds both halves of a message.
    /// </summary>
    /// <param name="preheader">
    /// The inbox preview line. Written deliberately, because it is the second thing read after
    /// the subject and the first thing that decides whether the message is opened.
    /// </param>
    /// <param name="paragraphs">Body copy. Plain sentences; no markup.</param>
    /// <param name="action">The single button, when there is one.</param>
    /// <param name="footnote">Small print under the rule — why they got this, what to ignore.</param>
    public static (string Html, string Text) Build(
        string heading,
        string preheader,
        IReadOnlyList<string> paragraphs,
        Action_? action = null,
        string? footnote = null,
        string? publicUrl = null)
    {
        var site = (publicUrl ?? PublicSite.Canonical).TrimEnd('/');

        return (
            Html(heading, preheader, paragraphs, action, footnote, site),
            Text(heading, paragraphs, action, footnote));
    }

    private static string Html(
        string heading,
        string preheader,
        IReadOnlyList<string> paragraphs,
        Action_? action,
        string? footnote,
        string site)
    {
        var sb = new StringBuilder();

        sb.Append($"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light">
            <meta name="supported-color-schemes" content="light">
            <title>{E(heading)}</title>
            </head>
            <body style="margin:0;padding:0;background:#f2f4f6;">

            <!-- The inbox preview line, then enough blank characters to stop the client pulling
                 body copy in after it. Hidden in every renderer that matters. -->
            <div style="display:none;font-size:1px;color:#f2f4f6;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">
            {E(preheader)}&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;&#847;&zwnj;&nbsp;
            </div>

            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#f2f4f6;">
            <tr><td align="center" style="padding:28px 12px;">

            <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px;max-width:100%;background:#ffffff;border-radius:14px;overflow:hidden;border:1px solid {Line};">

            <!-- Header. The band is the logo's own navy so the image has no visible edge, and the
                 wordmark is a link home with alt text that reads as the brand when images are
                 blocked — which is most first messages from a new sender. -->
            <tr><td style="background:{Navy};padding:22px 28px;">
            <a href="{E(site)}" style="text-decoration:none;">
            <img src="{E(site)}/brand/radar-wordmark.png" width="150" height="75" alt="Radar"
                 style="display:block;border:0;width:150px;height:auto;">
            </a>
            </td></tr>

            <tr><td style="padding:30px 28px 8px 28px;">
            <h1 style="margin:0;font-family:{Font};font-size:21px;line-height:1.35;font-weight:700;color:{Ink};letter-spacing:-0.01em;">{E(heading)}</h1>
            </td></tr>

            """);

        foreach (var paragraph in paragraphs)
        {
            sb.Append($"""
                <tr><td style="padding:0 28px 14px 28px;">
                <p style="margin:0;font-family:{Font};font-size:15px;line-height:1.65;color:{Body};">{E(paragraph)}</p>
                </td></tr>

                """);
        }

        if (action is not null)
        {
            // A table cell with a background, not an <a> with padding: Outlook drops padding on an
            // anchor, which collapses the button to bare underlined text.
            sb.Append($"""
                <tr><td style="padding:10px 28px 6px 28px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                <tr><td align="center" bgcolor="{Action}" style="border-radius:8px;">
                <a href="{E(action.Url)}" style="display:inline-block;padding:13px 26px;font-family:{Font};font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:8px;">{E(action.Label)}</a>
                </td></tr>
                </table>
                </td></tr>

                <!-- The same URL in full. A button is unclickable in a plain-text-preferring
                     client and unreachable when a security gateway rewrites the href, and a
                     reader who cannot read the destination cannot judge whether to trust it. -->
                <tr><td style="padding:4px 28px 18px 28px;">
                <p style="margin:0;font-family:{Font};font-size:12.5px;line-height:1.55;color:{Muted};word-break:break-all;">
                Or paste this into your browser:<br>{E(action.Url)}
                </p>
                </td></tr>

                """);
        }

        if (!string.IsNullOrWhiteSpace(footnote))
        {
            sb.Append($"""
                <tr><td style="padding:6px 28px 26px 28px;">
                <div style="border-top:1px solid {Line};padding-top:16px;">
                <p style="margin:0;font-family:{Font};font-size:12.5px;line-height:1.6;color:{Muted};">{E(footnote)}</p>
                </div>
                </td></tr>

                """);
        }

        sb.Append($"""
            </table>

            <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px;max-width:100%;">
            <tr><td style="padding:16px 28px;text-align:center;">
            <p style="margin:0;font-family:{Font};font-size:11.5px;line-height:1.6;color:{Muted};">
            Radar Systems Limited<br>
            <a href="{E(site)}/privacy" style="color:{Muted};">Privacy</a> &nbsp;&middot;&nbsp;
            <a href="{E(site)}/terms" style="color:{Muted};">Terms</a>
            </p>
            </td></tr>
            </table>

            </td></tr>
            </table>
            </body>
            </html>
            """);

        return sb.ToString();
    }

    /// <summary>
    /// The plain-text half, which is not a fallback.
    /// </summary>
    /// <remarks>
    /// It is what a screen reader in text mode, a watch, a terminal client and most spam filters
    /// actually read. A message with a good HTML part and an empty text part scores worse for
    /// delivery than one with both, so this carries the same content rather than "view in
    /// browser".
    /// </remarks>
    private static string Text(
        string heading, IReadOnlyList<string> paragraphs, Action_? action, string? footnote)
    {
        var sb = new StringBuilder();

        sb.AppendLine(heading);
        sb.AppendLine(new string('-', Math.Min(heading.Length, 60)));
        sb.AppendLine();

        foreach (var paragraph in paragraphs)
        {
            sb.AppendLine(paragraph);
            sb.AppendLine();
        }

        if (action is not null)
        {
            sb.AppendLine($"{action.Label}:");
            sb.AppendLine(action.Url);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(footnote))
        {
            sb.AppendLine(footnote);
            sb.AppendLine();
        }

        sb.AppendLine("--");
        sb.AppendLine("Radar Systems Limited");

        return sb.ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
