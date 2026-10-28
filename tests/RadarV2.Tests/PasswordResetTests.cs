using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A reset flow that leaks who has an account is worse than none.
///
/// There was no reset at all, so a user who mistyped a password at signup had no route back in —
/// which on a phone keyboard is not a rare event. These cover the parts that are easy to get
/// subtly wrong and impossible to notice from the outside.
/// </summary>
public class PasswordResetTests
{
    private static string Hash(string token)
    {
        var method = typeof(PasswordResetService).GetMethod(
            "Hash", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return (string)method!.Invoke(null, [token])!;
    }

    /// <summary>
    /// Only the hash is stored, so a leaked database is not also a set of working reset links.
    /// </summary>
    [Fact]
    public void TheStoredValueIsNotTheToken()
    {
        const string token = "7b1f4c2e9a";

        Assert.NotEqual(token, Hash(token));
        Assert.Equal(64, Hash(token).Length);
    }

    [Fact]
    public void HashingIsStableSoATokenCanBeRedeemed()
    {
        Assert.Equal(Hash("abc123"), Hash("abc123"));
    }

    [Fact]
    public void DifferentTokensHashDifferently()
    {
        Assert.NotEqual(Hash("abc123"), Hash("abc124"));
    }

    /// <summary>
    /// A token short enough to guess is not a token. 32 bytes of CSPRNG output is 64 hex
    /// characters, and the lifetime is what bounds a leaked link.
    /// </summary>
    [Fact]
    public void TheTokenIsLongAndShortLived()
    {
        var bytes = (int)typeof(PasswordResetService)
            .GetField("TokenBytes", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        var lifetime = (TimeSpan)typeof(PasswordResetService)
            .GetField("Lifetime", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.True(bytes >= 32, $"Only {bytes} bytes of entropy.");
        Assert.InRange(lifetime, TimeSpan.FromMinutes(15), TimeSpan.FromHours(24));
    }

    /// <summary>
    /// Email is a hard dependency of this flow, and its absence has to be visible.
    /// </summary>
    /// <remarks>
    /// A sender with no host reports itself unconfigured rather than accepting messages and
    /// dropping them, which would make the reset form appear to work and deliver nothing.
    /// </remarks>
    [Theory]
    [InlineData(null, null)]
    [InlineData("smtp.example.com", null)]
    [InlineData(null, "radar@example.com")]
    public void AnIncompleteEmailSenderReportsItselfUnconfigured(string? host, string? from)
    {
        var settings = new List<KeyValuePair<string, string?>>();
        if (host is not null) settings.Add(new("Email:SmtpHost", host));
        if (from is not null) settings.Add(new("Email:From", from));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var sender = new SmtpEmailSender(
            config, NullLogger<SmtpEmailSender>.Instance);

        Assert.False(sender.IsConfigured);
    }

    [Fact]
    public void AHostAndSenderIsEnough()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Email:SmtpHost", "smtp.example.com"),
                new("Email:From", "radar@example.com"),
            ])
            .Build();

        var sender = new SmtpEmailSender(
            config, NullLogger<SmtpEmailSender>.Instance);

        Assert.True(sender.IsConfigured);
    }
}
