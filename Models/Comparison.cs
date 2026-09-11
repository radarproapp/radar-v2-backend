namespace RadarV2.Models;

public class PolicyComparison
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> Items { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string? AiEdge { get; set; }
    public List<ComparisonRow> Rows { get; set; } = [];
    public string? Disclaimer { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ComparisonRow
{
    public string Label { get; set; } = string.Empty;
    public string ValueA { get; set; } = string.Empty;
    public string ValueB { get; set; } = string.Empty;
    public string? LabelA { get; set; }
    public string? LabelB { get; set; }
    public ComparisonRowTag Tag { get; set; }
}

public enum ComparisonRowTag
{
    Same,
    Differs,
    Context
}
