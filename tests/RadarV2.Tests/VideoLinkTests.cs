using System.Text.RegularExpressions;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Hardcoded YouTube ids rot, and these had rotted badly: of the 33 in the codebase, 21 returned
/// 404 from YouTube's oEmbed endpoint — 64% of every video Radar recommended was a dead link.
/// Several of the ids were not even plausible, which suggests they were invented rather than
/// copied.
///
/// A search URL cannot rot. It needs no API key and no quota, and it lands the learner on real
/// current results instead of a removed video.
/// </summary>
public class VideoLinkTests
{
    private static readonly Regex DirectVideoLink = new(@"youtube\.com/watch\?v=|youtu\.be/", RegexOptions.IgnoreCase);

    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("Data Analytics")]
    [InlineData("Software Engineering")]
    [InlineData("Nursing")]
    [InlineData("Commercial law")]
    public void No_catalogue_resource_links_straight_to_a_video_id(string subject)
    {
        var resources = LearningResourceCatalog.GetResourcesForSkill(subject);

        Assert.All(resources, r =>
            Assert.False(DirectVideoLink.IsMatch(r.Url ?? string.Empty),
                $"{subject}: '{r.Title}' links to a fixed video id ({r.Url}). Use a search URL."));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void No_stage_resource_links_straight_to_a_video_id(int stage)
    {
        foreach (var subject in new[] { "Cybersecurity", "Data Analytics", "Nursing" })
        {
            var resources = LearningResourceCatalog.GetResourcesForStage(stage, subject);

            Assert.All(resources, r =>
                Assert.False(DirectVideoLink.IsMatch(r.Url ?? string.Empty),
                    $"{subject} stage {stage}: '{r.Title}' links to a fixed video id ({r.Url})."));
        }
    }

    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("Data Analytics")]
    public void No_learn_hub_resource_links_straight_to_a_video_id(string skill)
    {
        var payload = LearnHubCatalog.GetPayload(skill);

        Assert.All(payload.Resources, r =>
        {
            Assert.False(DirectVideoLink.IsMatch(r.Url ?? string.Empty),
                $"{skill}: '{r.Title}' links to a fixed video id ({r.Url}).");
            Assert.False(DirectVideoLink.IsMatch(r.MediaUrl ?? string.Empty),
                $"{skill}: '{r.Title}' embeds a fixed video id ({r.MediaUrl}).");
        });
    }

    [Fact]
    public void No_resource_shows_a_thumbnail_for_a_video_it_does_not_link_to()
    {
        // A thumbnail URL carries its own video id. Dropping the link but keeping the image left
        // a broken picture above a working search link.
        var resources = LearningResourceCatalog.GetResourcesForSkill("Data Analytics");

        Assert.All(resources, r =>
        {
            if (string.IsNullOrWhiteSpace(r.ThumbnailUrl)) return;
            Assert.DoesNotContain("img.youtube.com/vi/", r.ThumbnailUrl);
        });
    }
}
