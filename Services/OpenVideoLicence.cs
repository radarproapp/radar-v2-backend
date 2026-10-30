namespace RadarV2.Services;

/// <summary>
/// Whether the person who made a video has said it may be reused.
/// </summary>
/// <remarks>
/// This is the gate on reading a video's words. YouTube gives no legitimate way to read a
/// third-party transcript — captions.download needs the uploader's own credentials, and taking
/// the timedtext endpoint or the audio breaks their terms — so lesson summaries there are written
/// from the description and the card says so.
///
/// Open platforms are different: they publish the licence the uploader chose, as data, next to
/// the video. Where that licence is a Creative Commons one, the author has said in advance that
/// the work may be reused. Reading the captions of such a video to write a summary of it is the
/// thing the licence exists to permit.
///
/// The important half is what this refuses. A video whose licence is unset is not
/// "probably fine" — it is a work whose author has granted nothing, and the absence of a stated
/// licence is the most common state on every platform. Measured across fifteen PeerTube results
/// for one subject, nine were unset.
/// </remarks>
public static class OpenVideoLicence
{
    /// <summary>
    /// The licences PeerTube records, by the id it stores.
    /// </summary>
    /// <remarks>
    /// One through six are the Creative Commons family and seven is a public domain dedication.
    /// Every one of them grants reproduction with attribution, which is what reading the words
    /// of a video needs. The non-commercial and no-derivatives variants restrict what may be
    /// done with a copy; writing a summary of what a video says is neither selling the video nor
    /// publishing an altered one.
    ///
    /// Anything outside this range, including an id that is absent, is treated as granting
    /// nothing.
    /// </remarks>
    private static readonly Dictionary<int, string> Known = new()
    {
        [1] = "CC BY",
        [2] = "CC BY-SA",
        [3] = "CC BY-ND",
        [4] = "CC BY-NC",
        [5] = "CC BY-NC-SA",
        [6] = "CC BY-NC-ND",
        [7] = "Public domain",
    };

    /// <summary>Whether this licence lets Radar read the video's own words.</summary>
    public static bool PermitsReading(int? licenceId) =>
        licenceId is not null && Known.ContainsKey(licenceId.Value);

    /// <summary>
    /// What to call the licence on screen, or null when nothing was granted.
    /// </summary>
    /// <remarks>
    /// Shown because it is owed. Every one of these licences asks for attribution, and a reader
    /// who wants to use what they found needs to know which terms came with it.
    /// </remarks>
    public static string? Name(int? licenceId) =>
        licenceId is not null && Known.TryGetValue(licenceId.Value, out var name) ? name : null;
}
