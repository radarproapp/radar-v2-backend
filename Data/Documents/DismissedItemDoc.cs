namespace RadarV2.Data.Documents;

public class DismissedItemDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;
}
