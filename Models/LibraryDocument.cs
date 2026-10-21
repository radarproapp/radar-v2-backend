namespace RadarV2.Models;

public class LibraryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Subtopic { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsAfrica { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the original document can still be opened.
    ///
    /// Spec 8.6 asks that the original always be accessible. A summary of a document the reader
    /// cannot reach is worse than no entry: they have to take Radar's word for what it said, and
    /// that is exactly the position the rest of this work has been getting out of. Computed from
    /// whether a URL is recorded rather than asserted - whether it still resolves is a question
    /// only a request can answer.
    /// </summary>
    public bool HasOriginal => !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// Years since publication, so "last five years" is a fact the caller can check rather than
    /// a filter applied silently.
    /// </summary>
    public int Age => Year > 0 ? Math.Max(0, DateTime.UtcNow.Year - Year) : int.MaxValue;
}
