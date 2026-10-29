using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A video reaches the judge with what YouTube says it is about.
///
/// This test exists because the line that passes the description was written, lost when the edit
/// containing it aborted partway, and then committed under a message stating it was there. The
/// code kept handing the judge a title and a channel name, the commit said otherwise, and nothing
/// caught the difference — the only tests of this built a candidate by hand and went straight to
/// the judge, so they exercised everything except the step that was broken.
/// </summary>
public class VideoResourceTests
{
    private static YouTubeVideoRef Ref(string? description) => new()
    {
        VideoId = "dQw4w9WgXcQ",
        Title = "Set up your first Facebook ad",
        Description = description,
        ChannelTitle = "Marketing Basics",
        PublishedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>
    /// The description is carried, because it is the only real account of the video Radar has.
    /// </summary>
    [Fact]
    public void AVideoCarriesWhatYouTubeSaysItIsAbout()
    {
        var resource = LessonResourceService.VideoResource(
            Ref("Walks through campaign objectives, audience targeting and budget."));

        Assert.Equal(
            "Walks through campaign objectives, audience targeting and budget.",
            resource.Summary);
    }

    /// <summary>A video without one carries nothing, rather than null.</summary>
    [Fact]
    public void AVideoWithoutADescriptionCarriesNothing()
    {
        Assert.Equal(string.Empty, LessonResourceService.VideoResource(Ref(null)).Summary);
    }

    /// <summary>
    /// And the rest of what a card needs, so a change here cannot quietly drop one.
    /// </summary>
    [Fact]
    public void AVideoCarriesWhatTheCardShows()
    {
        var resource = LessonResourceService.VideoResource(Ref("A description."));

        Assert.Equal(LessonResourceKind.Video, resource.Kind);
        Assert.Equal("Set up your first Facebook ad", resource.Title);
        Assert.Equal("Marketing Basics", resource.Source);
        Assert.Contains("dQw4w9WgXcQ", resource.Url);
        Assert.Contains("dQw4w9WgXcQ", resource.EmbedUrl);

        // Said on the card, because a summary written from a blurb and one written from watching
        // forty minutes read identically otherwise.
        Assert.Equal(SummarySource.Description, resource.SummarySource);
    }
}
