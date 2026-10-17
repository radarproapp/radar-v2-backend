namespace RadarV2.Models;

/// <summary>
/// Telemetry record for any intelligence activity processed through Radar's central AI engine.
/// </summary>
public class AiActivityLogDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Feature { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string Mode { get; set; } = "LLM"; // "LLM", "DeterministicBrain", "Cached"
    public bool Success { get; set; } = true;
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Summary { get; set; }
}

/// <summary>
/// Diagnostic status and telemetry of Radar's Central AI Engine.
/// </summary>
public class AiEngineStats
{
    public string EngineName { get; set; } = "Radar Central AI Engine";
    public string Model { get; set; } = "deepseek/deepseek-chat";
    public bool IsCloudLlmActive { get; set; }
    public decimal MonthlySpendUsd { get; set; }
    public decimal MonthlySpendCapUsd { get; set; } = 25.0m;
    public long TotalActivitiesProcessed { get; set; }
    public Dictionary<string, long> ActivityCounts { get; set; } = [];
    public List<AiActivityLogDoc> RecentActivities { get; set; } = [];
}
