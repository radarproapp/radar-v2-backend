using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The rules that make an email survive a real client.
///
/// None of these is a style preference. Each one is something that silently breaks the message in
/// a client somebody actually uses, and none of them is visible when the template is checked in a
/// browser — which is where a template is usually checked.
/// </summary>
public class EmailTemplateTests
{
    private static (string Html, string Text) Sample(EmailTemplate.Action_? action = null) =>
        EmailTemplate.Build(
            heading: "Choose a new password",
            preheader: "The link works once and expires in an hour.",
            paragraphs: ["Someone asked to reset the password for this Radar account."],
            action: action ?? new EmailTemplate.Action_("Set a new password", "https://radarproapp.com/reset-password?token=abc"),
            footnote: "If this was not you, ignore this message.",
            publicUrl: "https://radarproapp.com");

    /// <summary>
    /// Outlook renders through Word, which supports neither flexbox nor grid. A layout built on
    /// either collapses to one stacked column there.
    /// </summary>
    [Fact]
    public void TheLayoutUsesTablesRatherThanFlexOrGrid()
    {
        var (html, _) = Sample();

        Assert.Contains("<table", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display:flex", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("display:grid", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gmail strips a style block on forward and in clipped views, so anything load-bearing has
    /// to be on the element itself.
    /// </summary>
    [Fact]
    public void StylesAreInlineRatherThanInAStyleBlock()
    {
        var (html, _) = Sample();

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("style=\"margin:0", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Most clients block remote images on a first message from a new sender, so the logo must
    /// carry no information — and the reader must still know who sent it.
    /// </summary>
    [Fact]
    public void TheLogoHasAltTextAndCarriesNoMeaning()
    {
        var (html, text) = Sample();

        Assert.Contains("alt=\"Radar\"", html, StringComparison.Ordinal);
        // Everything needed is in the text part, which has no images at all.
        Assert.Contains("Choose a new password", text);
        Assert.Contains("reset-password?token=abc", text);
    }

    /// <summary>
    /// Outlook drops padding on an anchor, so a padded &lt;a&gt; collapses to bare underlined
    /// text. The background belongs on a table cell.
    /// </summary>
    [Fact]
    public void TheButtonIsATableCellRatherThanAPaddedAnchor()
    {
        var (html, _) = Sample();

        Assert.Contains("bgcolor=", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A button is unclickable in a text-preferring client and unreachable when a security
    /// gateway rewrites the href, so the destination is always shown in full.
    /// </summary>
    [Fact]
    public void TheDestinationIsAlwaysReadableAsText()
    {
        const string url = "https://radarproapp.com/reset-password?token=abc";
        var (html, text) = Sample();

        // Twice in the html: once as the href, once as readable text.
        Assert.True(Occurrences(html, url) >= 2, "The url should be shown as well as linked.");
        Assert.Contains(url, text);
    }

    /// <summary>
    /// Without a preheader the inbox preview shows whatever text comes first, which is usually
    /// the logo's alt text.
    /// </summary>
    [Fact]
    public void ThereIsAPreheaderBeforeAnythingElse()
    {
        var (html, _) = Sample();

        var preheader = html.IndexOf("The link works once", StringComparison.Ordinal);
        var heading = html.IndexOf("Choose a new password</h1>", StringComparison.Ordinal);

        Assert.True(preheader > 0, "No preheader.");
        Assert.True(preheader < heading, "The preheader must come before the body.");
    }

    /// <summary>
    /// The plain-text part is not a fallback — it is what a text-mode screen reader, a watch and
    /// most spam filters read, and a message without one is scored worse for delivery.
    /// </summary>
    [Fact]
    public void ThePlainTextPartCarriesTheSameContent()
    {
        var (_, text) = Sample();

        Assert.Contains("Someone asked to reset the password", text);
        Assert.Contains("If this was not you", text);
        Assert.DoesNotContain("<", text);
    }

    /// <summary>
    /// Copy arrives from the application, and a name or subject containing a bracket must not be
    /// able to close a tag.
    /// </summary>
    [Fact]
    public void CopyIsEscaped()
    {
        var (html, _) = EmailTemplate.Build(
            heading: "Welcome <script>alert(1)</script>",
            preheader: "x",
            paragraphs: ["A & B"]);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The header band is the logo's own navy. The app's --navy is #12161d, which is close enough
    /// to read as a mistake and far enough apart to show a seam where the image meets the band.
    /// </summary>
    [Fact]
    public void TheHeaderBandMatchesTheLogoBackground()
    {
        var (html, _) = Sample();

        Assert.Contains("#191a2e", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("background:#12161d", html, StringComparison.OrdinalIgnoreCase);
    }

    // No webfont: Outlook ignores one and several privacy clients block it, so the fallback is
    // what most readers see — which makes the fallback the design.
    [Fact]
    public void NoWebfontIsRequested()
    {
        var (html, _) = Sample();

        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@font-face", html, StringComparison.OrdinalIgnoreCase);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }
}
