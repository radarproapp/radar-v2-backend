using RadarV2.Helpers;

namespace RadarV2.Tests;

/// <summary>
/// A feed timestamp read "-1479554m ago" in a review screenshot. Relative time is easy to get
/// wrong at the boundaries, and it is on nearly every card.
/// </summary>
public class DateHelperTests
{
    [Fact]
    public void A_future_date_never_renders_as_negative()
    {
        // Ingested items sometimes carry a future PublishedAt. "-1479554m ago" is not a time.
        var result = DateTime.UtcNow.AddDays(30).Humanize();

        Assert.DoesNotContain("-", result);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(1, "just now")]
    public void Very_recent_reads_as_just_now(int minutesAgo, string expected)
    {
        Assert.Equal(expected, DateTime.UtcNow.AddMinutes(-minutesAgo).Humanize());
    }

    [Fact]
    public void Minutes_hours_and_days_are_distinguished()
    {
        Assert.EndsWith("m ago", DateTime.UtcNow.AddMinutes(-30).Humanize());
        Assert.EndsWith("h ago", DateTime.UtcNow.AddHours(-5).Humanize());
        Assert.EndsWith("d ago", DateTime.UtcNow.AddDays(-10).Humanize());
    }

    [Fact]
    public void Yesterday_is_named()
    {
        Assert.Equal("yesterday", DateTime.UtcNow.AddHours(-30).Humanize());
    }
}
