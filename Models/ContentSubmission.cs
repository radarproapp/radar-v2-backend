using MongoDB.Bson.Serialization.Attributes;

namespace RadarV2.Models;

/// <summary>
/// Something a participant made about Radar, somewhere else.
/// </summary>
/// <remarks>
/// The other half of the campaign, and the half Radar cannot measure. A video lives on TikTok or
/// YouTube or WhatsApp; nothing inside this product can see it, count its views, or know whether
/// it is any good. So a submission is a <em>claim</em> - a participant saying "I made this, here
/// it is" - and it stays a claim until a person looks at it and says otherwise.
///
/// That is why there is no automatic score anywhere in this file. A number Radar invented for a
/// video it has never seen would be the same fabrication as a relevance percentage computed from
/// nothing, and it would decide who wins a prize.
/// </remarks>
public class ContentSubmission
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The participant who made it.</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Their name at the time of submitting, so a board reads without a second query.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Where it is. Checked for being a URL and for nothing else.</summary>
    /// <remarks>
    /// Radar does not fetch it. Following a link a stranger submitted, from the server, is a
    /// request Radar's own address makes on somebody else's behalf - and the only thing it would
    /// buy is confirming the page exists, which a human reviewer does anyway.
    /// </remarks>
    public string Link { get; set; } = string.Empty;

    /// <summary>Where it was posted, as the participant describes it.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>What they say it shows.</summary>
    public string Note { get; set; } = string.Empty;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    // ── what a reviewer decided ──────────────────────────────────────────────

    /// <summary>
    /// A reviewer's mark out of ten, or null while nobody has looked.
    /// </summary>
    /// <remarks>
    /// Null is the honest state and is not a zero. An unjudged submission has not scored badly -
    /// it has not been looked at, and a board that renders the two the same way tells a
    /// participant their work was rejected when it is sitting in a queue.
    /// </remarks>
    [BsonIgnoreIfNull]
    public int? Score { get; set; }

    /// <summary>Why it got that mark, in the reviewer's words.</summary>
    /// <remarks>
    /// Required alongside a score by the endpoint that records one. A mark with no reason is a
    /// number a participant cannot argue with, which is the thing this product refuses to
    /// produce anywhere else and should not start producing here.
    /// </remarks>
    [BsonIgnoreIfNull]
    public string? Verdict { get; set; }

    /// <summary>Which admin judged it, and when.</summary>
    [BsonIgnoreIfNull]
    public string? JudgedByProfileId { get; set; }

    [BsonIgnoreIfNull]
    public DateTime? JudgedAt { get; set; }

    /// <summary>Whether a person has looked at this yet.</summary>
    [BsonIgnore]
    public bool IsJudged => Score is not null;
}
