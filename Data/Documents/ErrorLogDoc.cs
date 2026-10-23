namespace RadarV2.Data.Documents;

/// <summary>
/// An unhandled exception, kept where it can be read back.
/// </summary>
/// <remarks>
/// Error detail is withheld from production responses, because an error body there reaches real
/// users. The cost of that showed up immediately: the production brief started failing and the
/// only way to see why was the host's log dashboard — the same blind spot that made the staging
/// outage take an afternoon, just moved somewhere else.
///
/// So the exception is written here as well as logged. Users still get "something went wrong";
/// an admin can read what it actually was.
/// </remarks>
public class ErrorLogDoc
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public string ExceptionType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? InnerException { get; set; }

    /// <summary>The top frames only — enough to locate the fault, short enough to store.</summary>
    public List<string> Stack { get; set; } = [];

    /// <summary>Who hit it, when the request was authenticated. Never their email.</summary>
    public string? UserId { get; set; }
}
