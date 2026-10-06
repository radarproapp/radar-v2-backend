namespace RadarV2.Models;

/// <summary>
/// A progressive, human follow-up Radar offers based on what it has observed — the playbook's
/// "ask when there is a reason" behaviour. Purely supportive: the user can ignore it, and applying
/// it goes through the normal profile update path.
/// </summary>
public class FollowUpSuggestion
{
    public string Id { get; set; } = string.Empty;

    /// <summary>"interest" | "focus" | "context".</summary>
    public string Kind { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;
    public string? Detail { get; set; }

    /// <summary>"addInterest" | "setFocus" | "editProfile".</summary>
    public string Action { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = "OK";

    /// <summary>The value to apply (interest name or focus term), when the action needs one.</summary>
    public string? Value { get; set; }
}
