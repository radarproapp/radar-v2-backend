namespace RadarV2.Models;

public class SubscriptionPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public bool IsPro { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public string Currency { get; set; } = "₦";
    public List<string> Features { get; set; } = [];
    public string? Badge { get; set; }
    public string? Note { get; set; }
}
