using System.Text.Json;

namespace RadarV2.Tests;

/// <summary>
/// The site Radar is served from has to be allowed to call the API.
///
/// This failure is invisible from the server. The domain went live serving the app, every page
/// loaded, and nothing worked: the origin was in neither the allowed list nor the preview-host
/// wildcard, so the browser refused every call before it was made. No request reached the API, so
/// no log, no error rate and no health check said anything was wrong. A list that quietly stops
/// naming the live site puts it straight back there.
/// </summary>
public class CorsOriginTests
{
    /// <summary>Where the API's own settings live, found from wherever the tests are run.</summary>
    private static string SettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "appsettings.json");
    }

    private static string[] AllowedOrigins()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath()));

        return document.RootElement
            .GetProperty("Cors")
            .GetProperty("AllowedOrigins")
            .EnumerateArray()
            .Select(o => o.GetString() ?? string.Empty)
            .ToArray();
    }

    /// <summary>
    /// Both hosts, because both answer. A list with only the apex in it leaves anybody who typed
    /// the www form with a site that loads and does nothing.
    /// </summary>
    [Theory]
    [InlineData("https://radarproapp.com")]
    [InlineData("https://www.radarproapp.com")]
    public void TheSiteRadarIsServedFromCanCallTheApi(string origin)
    {
        Assert.Contains(origin, AllowedOrigins());
    }

    /// <summary>
    /// Local development too. Dropping these is less dangerous than dropping the live site, but it
    /// is the same list and the same silent symptom.
    /// </summary>
    [Fact]
    public void TheDevelopmentServerCanCallTheApi()
    {
        Assert.Contains("http://localhost:5173", AllowedOrigins());
    }

    /// <summary>
    /// The two lists have to agree.
    /// </summary>
    /// <remarks>
    /// Emails address links to <see cref="RadarV2.Services.PublicSite.Canonical"/>, and the page
    /// that opens has to be able to call the API. If that host is not an allowed origin, a reset
    /// link opens a page that loads and then cannot do the one thing it exists to do — which is
    /// the same silent failure as the whole site being locked out, just narrowed to the people who
    /// have forgotten their password.
    /// </remarks>
    [Fact]
    public void TheHostEmailedLinksPointAtCanCallTheApi()
    {
        Assert.Contains(RadarV2.Services.PublicSite.Canonical, AllowedOrigins());
    }

    /// <summary>
    /// Nothing dead in the list. A stale host is not harmful on its own, but it is what made the
    /// list look like it had been maintained while the live site was missing from it.
    /// </summary>
    [Fact]
    public void NothingInTheListIsAHostRadarNoLongerUses()
    {
        Assert.DoesNotContain(
            AllowedOrigins(),
            o => o.Contains("netlify.app", StringComparison.OrdinalIgnoreCase));
    }
}
