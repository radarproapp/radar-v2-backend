namespace RadarV2.Data.Documents;

/// <summary>
/// How far through a video or podcast somebody actually got.
/// </summary>
/// <remarks>
/// Nothing recorded this. Playback was an iframe and a bare &lt;audio controls&gt;, so closing the
/// tab lost your place, and there was no answer to "how much of this did you listen to" — which
/// is the one thing a listening history is made of.
///
/// Two numbers, not one, and they measure different things:
///
/// <list type="bullet">
/// <item><see cref="PositionSeconds"/> is where to resume. It moves backwards when somebody
/// scrubs back, which is correct for a resume point and useless as a measure of attention.</item>
/// <item><see cref="SecondsPlayed"/> only ever increases, and only by time that actually
/// elapsed while playing. Scrubbing to the end does not earn forty minutes of listening.</item>
/// </list>
///
/// The distinction is what keeps the graph honest: "4h 12m listened" has to mean listened.
/// </remarks>
public class PlaybackProgressDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string UserId { get; set; } = string.Empty;

    public string ContentItemId { get; set; } = string.Empty;

    /// <summary>Where to pick up, in seconds from the start.</summary>
    public double PositionSeconds { get; set; }

    /// <summary>Total length, when the player knows it. Zero for a stream that never reported one.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Time genuinely spent playing, accumulated across sessions. Never decreases.</summary>
    public double SecondsPlayed { get; set; }

    /// <summary>
    /// Set once, the first time the item is played far enough through to count as finished.
    /// Stored rather than derived from the ratio, because duration can change when a feed
    /// re-publishes an episode and a finished item must not un-finish itself.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
