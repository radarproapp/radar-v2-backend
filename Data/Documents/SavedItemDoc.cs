namespace RadarV2.Data.Documents;

public class SavedItemDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}
