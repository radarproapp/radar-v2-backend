using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// An unsubscribe link has to work, and must only work for the person it was made for.
///
/// It has to work for somebody not signed in, on a device that has never been signed in, months
/// later — a link that stops working is a user who cannot opt out, which is the complaint that
/// gets a sending domain blocked. And it must not be alterable into somebody else's.
/// </summary>
public class UnsubscribeTokenTests
{
    private const string Key = "a-signing-key-long-enough-to-be-realistic-0123456789";

    [Fact]
    public void ATokenReadsBackAsWhatItWasMadeFrom()
    {
        var token = UnsubscribeToken.Create("profile-1", UnsubscribeToken.DailyDigest, Key);
        var read = UnsubscribeToken.Read(token, Key);

        Assert.NotNull(read);
        Assert.Equal("profile-1", read!.Value.UserId);
        Assert.Equal(UnsubscribeToken.DailyDigest, read.Value.Kind);
    }

    /// <summary>
    /// The whole point. Editing the id to somebody else's invalidates the signature, so one
    /// person's link cannot be turned into an unsubscribe for another.
    /// </summary>
    [Fact]
    public void AnAlteredTokenDoesNotVerify()
    {
        var mine = UnsubscribeToken.Create("profile-1", UnsubscribeToken.DailyDigest, Key);
        var theirs = UnsubscribeToken.Create("profile-2", UnsubscribeToken.DailyDigest, Key);

        // Their payload, my signature.
        var forged = $"{theirs.Split('.')[0]}.{mine.Split('.')[1]}";

        Assert.Null(UnsubscribeToken.Read(forged, Key));
    }

    [Fact]
    public void ATokenFromAnotherKeyDoesNotVerify()
    {
        var token = UnsubscribeToken.Create("profile-1", UnsubscribeToken.DailyDigest, Key);

        Assert.Null(UnsubscribeToken.Read(token, "a-completely-different-signing-key"));
    }

    /// <summary>
    /// The kind is inside the signature, so a link that stops the daily update cannot be edited
    /// into one that stops a password reset.
    /// </summary>
    [Fact]
    public void TheKindCannotBeSwapped()
    {
        var daily = UnsubscribeToken.Create("profile-1", UnsubscribeToken.DailyDigest, Key);
        var other = UnsubscribeToken.Create("profile-1", "security", Key);

        Assert.NotEqual(daily, other);
        Assert.Equal("security", UnsubscribeToken.Read(other, Key)!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rubbish")]
    [InlineData("no-dot-separator")]
    [InlineData("!!!.!!!")]
    public void RubbishIsRejectedRatherThanThrowing(string? token)
    {
        Assert.Null(UnsubscribeToken.Read(token, Key));
    }

    /// <summary>
    /// Base64url, so the token survives a query string, an href and a line wrap in the plain-text
    /// part — all three of which mangle raw base64.
    /// </summary>
    [Fact]
    public void ATokenIsSafeInAUrl()
    {
        for (var i = 0; i < 50; i++)
        {
            var token = UnsubscribeToken.Create(Guid.NewGuid().ToString(), UnsubscribeToken.DailyDigest, Key);

            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
            Assert.Equal(token, Uri.EscapeDataString(token));
        }
    }

    // A profile id containing the separator must still round-trip.
    [Fact]
    public void AnIdContainingAColonStillReadsBack()
    {
        var token = UnsubscribeToken.Create("tenant:7:profile", UnsubscribeToken.DailyDigest, Key);
        var read = UnsubscribeToken.Read(token, Key);

        Assert.Equal("tenant:7:profile", read!.Value.UserId);
        Assert.Equal(UnsubscribeToken.DailyDigest, read.Value.Kind);
    }

    // Off by default: daily is the most aggressive cadence available, and opting somebody in on
    // their behalf costs the whole sending domain when it goes wrong.
    [Fact]
    public void TheDailyDigestIsOffUntilAskedFor()
    {
        Assert.False(new RadarV2.Models.NotificationPrefs().DailyDigest);
    }
}
