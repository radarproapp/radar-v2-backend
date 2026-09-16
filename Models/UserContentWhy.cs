namespace RadarV2.Models;

/// <summary>
/// A cached, personalized "why this matters" sentence for one (user, content item) pair —
/// the per-user gate artifact called for in the RADAR product-strategy notes §3.3. Distinct
/// from ContentItem.WhyItMatters, which is generated once at ingestion and is the same for
/// every user.
/// </summary>
public class UserContentWhy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string WhyText { get; set; } = string.Empty;

    /// <summary>Profile fields the sentence was generated against — used to detect staleness.</summary>
    public string GoalSnapshot { get; set; } = string.Empty;
    public string PersonaSnapshot { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public bool? IsHelpful { get; set; }
    public DateTime? RatedAt { get; set; }

    /// <summary>
    /// True only when WhyText was actually produced by the LLM for this user. False when this
    /// row exists solely because the user rated the generic fallback text (no LLM key/budget
    /// available at rating time) — keeps the "why helpful rate" metric honest about what was
    /// actually rated.
    /// </summary>
    public bool IsGenerated { get; set; }
}
