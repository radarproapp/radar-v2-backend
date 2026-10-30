namespace RadarV2.Models;

/// <summary>
/// A video from an open platform, with the licence its author chose.
/// </summary>
/// <remarks>
/// Separate from <see cref="YouTubeVideoRef"/> rather than folded into it, because the difference
/// between them is the whole point: this one states what may be done with it. A YouTube video
/// carries no licence Radar can act on, so a summary of one is written from its description. A
/// video whose author licensed it under Creative Commons can be read.
/// </remarks>
public class OpenVideoRef
{
    public string Uuid { get; set; } = string.Empty;

    /// <summary>The instance it lives on, which is where its captions are served from.</summary>
    public string Host { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
    public string EmbedUrl { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;

    public DateTime? PublishedAt { get; set; }
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// The licence the author chose, as the platform stores it. Null when they chose none.
    /// </summary>
    /// <remarks>
    /// Null is the common case and it means nothing was granted — not that the video is free to
    /// use. See <see cref="Services.OpenVideoLicence"/>.
    /// </remarks>
    public int? LicenceId { get; set; }
}
