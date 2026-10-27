using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The store reports itself configured only when it can actually store something.
///
/// The specific mistake being avoided: isCloudLlmActive read "an api key string is present" and
/// so reported a live engine while every call failed 401. A store with credentials and no bucket
/// name is in the same position — it looks ready and cannot write.
/// </summary>
public class ObjectStoreTests
{
    private static R2ObjectStore Store(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        return new R2ObjectStore(config, new StubHttpClientFactory(), NullLogger<R2ObjectStore>.Instance);
    }

    private static (string, string)[] Complete =>
    [
        ("R2:AccountId", "acct"),
        ("R2:AccessKeyId", "akid"),
        ("R2:SecretAccessKey", "secret"),
        ("R2:Bucket", "radar"),
    ];

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public void NothingConfiguredIsNotConfigured()
    {
        Assert.False(Store().IsConfigured);
    }

    [Theory]
    [InlineData("R2:AccountId")]
    [InlineData("R2:AccessKeyId")]
    [InlineData("R2:SecretAccessKey")]
    [InlineData("R2:Bucket")]
    public void AnyOneSettingMissingIsNotConfigured(string omit)
    {
        var settings = Complete.Where(s => s.Item1 != omit).ToArray();

        Assert.False(Store(settings).IsConfigured, $"Reported configured without {omit}.");
    }

    [Fact]
    public void AllFourSettingsIsConfigured()
    {
        Assert.True(Store(Complete).IsConfigured);
    }

    // No credentials means no url, rather than an unsigned one that 403s on use.
    [Fact]
    public void NoSignedUrlWithoutCredentials()
    {
        Assert.Null(Store().SignedUrl("transcripts/abc.txt", TimeSpan.FromMinutes(10)));
    }

    /// <summary>
    /// The signature travels in the query string, because the URL is handed to a browser, an
    /// audio element or an img tag — none of which can attach an Authorization header.
    /// </summary>
    [Fact]
    public void ASignedUrlCarriesItsSignatureInTheQuery()
    {
        var url = Store(Complete).SignedUrl("transcripts/abc.txt", TimeSpan.FromMinutes(10));

        Assert.NotNull(url);
        Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", url);
        Assert.Contains("X-Amz-Signature=", url);
        Assert.Contains("X-Amz-Expires=600", url);
        Assert.Contains("/radar/transcripts/abc.txt", url);
    }

    /// <summary>
    /// Slashes stay slashes in the key, because a key is a path — escaping them would bury
    /// everything in one object with %2F in its name.
    /// </summary>
    /// <remarks>
    /// Asserted against the path only. SigV4 *requires* the credential parameter to be escaped,
    /// so it legitimately contains %2F and a whole-url check fails on correct output — which is
    /// what my first version of this did.
    /// </remarks>
    [Fact]
    public void KeySlashesArePreserved()
    {
        var url = Store(Complete).SignedUrl("podcasts/2026/episode-12.txt", TimeSpan.FromMinutes(5));

        Assert.NotNull(url);
        var path = new Uri(url!).AbsolutePath;

        Assert.Equal("/radar/podcasts/2026/episode-12.txt", path);
        Assert.DoesNotContain("%2F", path);
    }

    // R2's maximum is seven days; a longer request is clamped rather than silently rejected later.
    [Fact]
    public void ExpiryIsClampedToWhatR2Accepts()
    {
        var url = Store(Complete).SignedUrl("k", TimeSpan.FromDays(30));

        Assert.Contains("X-Amz-Expires=604800", url);
    }

    [Fact]
    public void TheSameKeySignedTwiceWithDifferentExpiriesDiffers()
    {
        var store = Store(Complete);

        var short_ = store.SignedUrl("k", TimeSpan.FromMinutes(1));
        var long_ = store.SignedUrl("k", TimeSpan.FromHours(1));

        Assert.NotEqual(short_, long_);
    }
}
