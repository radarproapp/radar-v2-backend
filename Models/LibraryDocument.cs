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
}
