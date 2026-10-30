using RadarV2.Models;
using RadarV2.Services;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// Reading a video's own words, where its author said that is allowed.
/// </summary>
/// <remarks>
/// YouTube gives no legitimate route to a third-party transcript: captions.download needs the
/// uploader's own credentials, and taking the timedtext endpoint or the audio breaks their terms.
/// So lesson summaries there are written from the description, and the card says so.
///
/// Open platforms publish the licence the uploader chose, as data. Where that is a Creative
/// Commons licence, the author has said in advance the work may be reused, and reading its
/// captions to write a summary is what the licence exists to permit.
/// </remarks>
public class OpenVideoTests
{
    /// <summary>
    /// A licence nobody set grants nothing.
    /// </summary>
    /// <remarks>
    /// This is the half that matters. An unset licence is the most common state on every
    /// platform - nine of fifteen results for one subject - and it means the author has granted
    /// nothing, not that the video is free to use.
    /// </remarks>
    [Fact]
    public void AVideoWithNoLicenceMayNotBeRead()
    {
        Assert.False(OpenVideoLicence.PermitsReading(null));
        Assert.False(OpenVideoLicence.PermitsReading(0));
        Assert.Null(OpenVideoLicence.Name(null));
    }

    /// <summary>Every Creative Commons licence grants reproduction with attribution.</summary>
    [Theory]
    [InlineData(1, "CC BY")]
    [InlineData(2, "CC BY-SA")]
    [InlineData(5, "CC BY-NC-SA")]
    [InlineData(7, "Public domain")]
    public void AnOpenLicenceMayBeRead(int licenceId, string expected)
    {
        Assert.True(OpenVideoLicence.PermitsReading(licenceId));
        Assert.Equal(expected, OpenVideoLicence.Name(licenceId));
    }

    /// <summary>And an id outside the ones the platform records is not assumed to be one.</summary>
    [Fact]
    public void AnUnrecognisedLicenceIsNotGuessedAt()
    {
        Assert.False(OpenVideoLicence.PermitsReading(99));
        Assert.Null(OpenVideoLicence.Name(99));
    }

    // ── Subtitles into words ────────────────────────────────────────────────

    /// <summary>
    /// A caption file is turned into what was said.
    /// </summary>
    /// <remarks>
    /// WebVTT interleaves cue numbers and timestamps with the words. Left in, a model
    /// summarising the file spends its attention on arrows and clock times.
    /// </remarks>
    [Fact]
    public void TheTimingsAreNotPartOfWhatWasSaid()
    {
        const string vtt = """
            WEBVTT

            1
            00:00:01.000 --> 00:00:04.000
            Prompt engineering is how you ask a model for something.

            2
            00:00:04.000 --> 00:00:08.000
            The wording changes the answer more than people expect.
            """;

        var spoken = PeerTubeClient.Spoken(vtt);

        Assert.Contains("Prompt engineering is how you ask", spoken);
        Assert.Contains("wording changes the answer", spoken);
        Assert.DoesNotContain("-->", spoken);
        Assert.DoesNotContain("WEBVTT", spoken);
        Assert.DoesNotContain("00:00", spoken);
    }

    /// <summary>
    /// A rolling caption repeats each line, and the repeats are dropped.
    /// </summary>
    /// <remarks>
    /// Automatic captions scroll: every line appears again in the next cue as the one above it
    /// moves up. Kept, they triple the length of the text for no added meaning, and a summary
    /// written from them reads as though the speaker stammered.
    /// </remarks>
    [Fact]
    public void ARollingCaptionIsNotSaidThreeTimes()
    {
        const string vtt = """
            WEBVTT

            00:00:01.000 --> 00:00:03.000
            the first thing to understand

            00:00:03.000 --> 00:00:05.000
            the first thing to understand

            00:00:05.000 --> 00:00:07.000
            is that the model has no memory
            """;

        var spoken = PeerTubeClient.Spoken(vtt);

        Assert.Equal(
            "the first thing to understand is that the model has no memory",
            spoken);
    }

    /// <summary>Speaker markup is not speech.</summary>
    [Fact]
    public void MarkupIsStripped()
    {
        Assert.Equal("hello there", PeerTubeClient.Spoken("<v Speaker>hello there</v>"));
    }

    /// <summary>Nothing in means nothing out, rather than a crash.</summary>
    [Fact]
    public void NothingInMeansNothingOut()
    {
        Assert.Equal(string.Empty, PeerTubeClient.Spoken(null));
        Assert.Equal(string.Empty, PeerTubeClient.Spoken("   "));
        Assert.Equal(string.Empty, PeerTubeClient.Spoken("WEBVTT\n\n1\n00:00:01.000 --> 00:00:02.000\n"));
    }

    // ── A video as a lesson resource ────────────────────────────────────────

    /// <summary>
    /// An open video becomes a candidate with its own player and channel.
    /// </summary>
    /// <remarks>
    /// Until this existed every lesson video came from one source, so a subject YouTube ranks
    /// poorly for had no videos at all - and a lesson with no videos is the complaint this path
    /// exists to answer.
    /// </remarks>
    [Fact]
    public void AnOpenVideoBecomesALessonResource()
    {
        var resource = LessonResourceService.VideoResource(new OpenVideoRef
        {
            Uuid = "abc",
            Host = "tv.example.org",
            Title = "Asking a model for what you want",
            Description = "A walk through writing prompts that get usable answers.",
            ChannelTitle = "Open Learning",
            Url = "https://tv.example.org/videos/watch/abc",
            EmbedUrl = "https://tv.example.org/videos/embed/abc",
            LicenceId = 1,
        });

        Assert.Equal(LessonResourceKind.Video, resource.Kind);
        Assert.Equal("Asking a model for what you want", resource.Title);
        Assert.Equal("Open Learning", resource.Source);
        Assert.Equal("https://tv.example.org/videos/embed/abc", resource.EmbedUrl);

        // The description is what it starts with, and the card says so until a transcript is read.
        Assert.Equal(SummarySource.Description, resource.SummarySource);
        Assert.Contains("writing prompts", resource.Summary);
    }
}
