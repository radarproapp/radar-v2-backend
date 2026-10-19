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
    /// <summary>
    /// True only when Radar is genuinely calling the cloud model right now. A configured key is not
    /// enough: once the monthly spend cap is reached the engine silently switches to the
    /// deterministic brain, and reporting "active" there would show green on the dashboard while
    /// output quality had already collapsed.
    /// </summary>
    public bool IsCloudLlmActive { get; set; }

    /// <summary>Key is configured, but the monthly spend cap has stopped us using it.</summary>
    public bool IsBudgetExhausted { get; set; }

    /// <summary>Set when the engine is not using the cloud model, saying plainly why.</summary>
    public string? DegradedReason { get; set; }
    public decimal MonthlySpendUsd { get; set; }
    public decimal MonthlySpendCapUsd { get; set; } = 25.0m;
    public long TotalActivitiesProcessed { get; set; }
    public Dictionary<string, long> ActivityCounts { get; set; } = [];
    public List<AiActivityLogDoc> RecentActivities { get; set; } = [];
}
