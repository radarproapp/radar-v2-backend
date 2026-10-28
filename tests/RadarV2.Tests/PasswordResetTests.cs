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

    private static SendByteEmailSender SendByte(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

        return new SendByteEmailSender(config, new StubFactory(), NullLogger<SendByteEmailSender>.Instance);
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = new Uri("https://api.sendbyte.africa/") };
    }

    /// <summary>
    /// A key with nowhere to send from cannot send, so both settings are required.
    /// </summary>
    /// <remarks>
    /// The same rule the object store and the OpenRouter status field both had to learn: reporting
    /// configured because one string is present means reporting a working sender that fails on
    /// every call.
    /// </remarks>
    [Theory]
    [InlineData(null, null)]
    [InlineData("sk_live_abc", null)]
    [InlineData(null, "radar@radarproapp.com")]
    public void AnIncompleteSendByteSenderIsNotConfigured(string? key, string? from)
    {
        var settings = new List<(string, string)>();
        if (key is not null) settings.Add(("SendByte:ApiKey", key));
        if (from is not null) settings.Add(("SendByte:From", from));

        Assert.False(SendByte([.. settings]).IsConfigured);
    }

    [Fact]
    public void AKeyAndASenderIsEnough()
    {
        var sender = SendByte(("SendByte:ApiKey", "sk_live_abc"), ("SendByte:From", "radar@radarproapp.com"));

        Assert.True(sender.IsConfigured);
    }

    /// <summary>
    /// A sandbox key is accepted, returns an id, and delivers nothing. From the application's side
    /// that is indistinguishable from working, so it has to be detectable.
    /// </summary>
    [Fact]
    public void ASandboxKeyIsRecognised()
    {
        var test = SendByte(("SendByte:ApiKey", "sk_test_abc"), ("SendByte:From", "radar@radarproapp.com"));
        var live = SendByte(("SendByte:ApiKey", "sk_live_abc"), ("SendByte:From", "radar@radarproapp.com"));

        Assert.True(test.IsSandbox);
        Assert.False(live.IsSandbox);
    }

    /// <summary>
    /// The SMTP relay reads SendByte's credentials, so switching to it needs a host and nothing
    /// else — the username is "apikey" by convention and the password is the API key.
    /// </summary>
    [Fact]
    public void TheSmtpRelayFallsBackToSendByteCredentials()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new("Email:SmtpHost", "smtp.sendbyte.africa"),
                new("SendByte:ApiKey", "sk_live_abc"),
                new("SendByte:From", "radar@radarproapp.com"),
            ])
            .Build();

        var sender = new SmtpEmailSender(config, NullLogger<SmtpEmailSender>.Instance);

        Assert.True(sender.IsConfigured);
    }
}
