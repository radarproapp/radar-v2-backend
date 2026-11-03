using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A link somebody pasted, checked properly.
/// </summary>
/// <remarks>
/// This job was being done by <c>[Url]</c>, and three comments in two files said so. It is a
/// scheme-prefix test: measured, it accepts a bare "https://", markup in the path, a newline in
/// the middle, and credentials that make "ok.com" a different host - while rejecting a link with
/// a leading space that the service was about to trim one line later.
///
/// Nothing in Radar fetches a submitted link, so none of that is an SSRF. What it is, is a
/// stored value the model promised was safe, heading for an href in a reviewer's browser.
/// </remarks>
public class SubmittedLinkTests
{
    [Theory]
    [InlineData("https://www.tiktok.com/@ada/video/123")]
    [InlineData("http://youtu.be/abc123")]
    [InlineData("https://x.com/ada/status/1?s=20")]
    [InlineData("https://instagram.com/p/abc/")]
    public void ALinkSomebodyCanOpenIsKept(string link) =>
        Assert.Equal(link, SubmittedLink.Clean(link));

    /// <summary>Trimmed before it is judged, not after.</summary>
    /// <remarks>
    /// The old order refused "   https://ok.com/x" and then the service trimmed it one line
    /// later, so a link that was about to be valid was rejected for the whitespace around it.
    /// </remarks>
    [Fact]
    public void SurroundingSpaceIsTrimmedRatherThanTreatedAsAFault() =>
        Assert.Equal("https://ok.com/x", SubmittedLink.Clean("   https://ok.com/x\t"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]                                   // accepted by [Url]
    [InlineData("tiktok.com/@ada/video/1")]                    // no scheme
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("ftp://files.example.com/x")]                  // accepted by [Url]
    [InlineData("/etc/passwd")]
    [InlineData("../../admin")]
    [InlineData("mailto:somebody@example.com")]
    public void SomethingThatIsNotALinkIsNotQuietlyStored(string? raw) =>
        Assert.Null(SubmittedLink.Clean(raw));

    /// <summary>
    /// The shapes the old check let through.
    /// </summary>
    /// <remarks>
    /// Every one of these was measured as accepted by <c>[Url]</c>. The last two are a host with
    /// no dot in it - a machine on whoever is looking at this - and a url whose credentials make
    /// the host something other than what it reads as.
    /// </remarks>
    [Theory]
    [InlineData("https://ok.com/<script>alert(1)</script>")]
    [InlineData("https://ok.com/\" onmouseover=\"alert(1)")]
    [InlineData("https://a b c.com/x")]
    [InlineData("http:// javascript:alert(1)")]
    [InlineData("http://\njavascript:alert(1)")]
    [InlineData("https://localhost:5000/api/admin/users")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://user:pw@evil.com@ok.com/")]
    public void TheShapesTheOldCheckLetThroughAreRefused(string raw) =>
        Assert.Null(SubmittedLink.Clean(raw));

    /// <summary>
    /// The same video, pasted from two apps, is the same video.
    /// </summary>
    /// <remarks>
    /// Five copies of one link was free and it used to raise a participant's standing, because
    /// the board summed their marks. The key is what the unique index holds.
    /// </remarks>
    [Fact]
    public void TheSameVideoTwoWaysMakesOneKey()
    {
        var key = SubmittedLink.KeyFor("https://www.TikTok.com/@Ada/video/1/");

        Assert.Equal(key, SubmittedLink.KeyFor("https://www.tiktok.com/@ada/video/1"));
        Assert.NotEqual(key, SubmittedLink.KeyFor("https://www.tiktok.com/@ada/video/2"));
    }
}
