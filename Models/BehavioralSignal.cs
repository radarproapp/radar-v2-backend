namespace RadarV2.Models;

/// <summary>
/// One evolving, per-user signal inferred from behaviour or declared by the user.
/// Declared and inferred signals are kept separate internally; explicit negative feedback
/// overrides weak behavioural inference. Recency is applied at read time via
/// <see cref="Services.BehavioralSignalService.DecayedStrength"/>.
/// </summary>
public class BehavioralSignal
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;

    /// <summary>The topic, capability, industry or interest this signal describes.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Declared (explicitly stated by the user) or Inferred (observed from behaviour).</summary>
    public SignalSource Source { get; set; } = SignalSource.Inferred;

    /// <summary>0..1 accumulated strength. Grows gradually with repeated positive behaviour.</summary>
    public double Strength { get; set; }

    /// <summary>0..1 confidence that this signal reflects a real, stable preference.</summary>
    public double Confidence { get; set; }

    /// <summary>Net explicit feedback (-1..1). Negative values suppress this signal over inference.</summary>
    public double ExplicitFeedback { get; set; }

    public int PositiveCount { get; set; }
    public int NegativeCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum SignalSource
{
    Declared,
    Inferred
}
