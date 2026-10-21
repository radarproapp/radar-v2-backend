using RadarV2.Models;

namespace RadarV2.Tests;

public class OpportunityDeadlineTests
{
    private static Opportunity Closing(double daysFromNow) =>
        new() { Deadline = DateTime.UtcNow.AddDays(daysFromNow) };

    [Fact]
    public void A_closed_opportunity_is_not_called_urgent()
    {
        // DaysUntilDeadline floors at zero, so a deadline that passed last week arrived as 0 and
        // matched the "<= 7" rung. Users were told to hurry for something already shut.
        var closed = Closing(-7);

        Assert.True(closed.HasClosed);
        Assert.Contains("Closed", closed.UrgencyLabel);
        Assert.DoesNotContain("Urgent", closed.UrgencyLabel);
    }

    [Fact]
    public void One_minute_past_the_deadline_is_closed_not_due_today()
    {
        var justClosed = new Opportunity { Deadline = DateTime.UtcNow.AddMinutes(-1) };

        Assert.True(justClosed.HasClosed);
        Assert.Contains("Closed", justClosed.UrgencyLabel);
    }

    [Fact]
    public void No_deadline_is_not_described_as_recently_opened()
    {
        // Nothing in the model knows when anything opened, so that label was invented.
        var noDeadline = new Opportunity { Deadline = null };

        Assert.False(noDeadline.HasClosed);
        Assert.DoesNotContain("Recently opened", noDeadline.UrgencyLabel);
        Assert.Contains("No deadline", noDeadline.UrgencyLabel);
    }

    [Theory]
    [InlineData(1, "Urgent")]
    [InlineData(6, "Urgent")]
    [InlineData(14, "2 weeks left")]
    [InlineData(60, "Open")]
    public void An_open_deadline_is_described_by_how_long_is_left(double days, string expected)
    {
        Assert.Contains(expected, Closing(days).UrgencyLabel);
    }

    [Fact]
    public void A_future_deadline_is_never_marked_closed()
    {
        Assert.False(Closing(0.5).HasClosed);
        Assert.False(Closing(365).HasClosed);
    }
}
