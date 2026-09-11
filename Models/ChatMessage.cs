namespace RadarV2.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<ContentItem> SuggestedResources { get; set; } = [];
}

public class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = [];
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}
