namespace RadarV2.Models;

public class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> LinkedItemIds { get; set; } = [];
    public List<string> LinkedSourceIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime EditedAt { get; set; } = DateTime.UtcNow;

    // ── Google Keep behaviours ───────────────────────────────────────────────
    // The old note was a title and a body behind a separate editor page, which is why it read as
    // "I don't understand the note feature": there was nothing to do with a note once written.
    // These are the few properties that make a note feel like somewhere you actually put things.

    /// <summary>Pinned notes sort above everything else, regardless of when they were edited.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Hidden from the main grid without being destroyed.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Named colour, not a hex value. The UI owns the palette, so notes stay readable if the theme
    /// changes and a stored colour can never be unreadable against a future background.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Checklist items. A note is either prose or a list; Keep's trick is that you can switch, and
    /// a half-finished list is the most common kind of note anyone actually keeps.
    /// </summary>
    public List<NoteChecklistItem> Checklist { get; set; } = [];
}

public class NoteChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
}
