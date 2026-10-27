using System.Reflection;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// What the deterministic brain says when the cloud call fails has to be distinguishable from a
/// real answer.
///
/// The engine falls back on every failure, and on staging it was falling back on every single
/// call: OpenRouter answered HTTP 402 because no max_tokens was sent, so each request reserved
/// the model's full 16,384-token allowance and was refused on a key that could afford 15,174 —
/// with credit left and most of the monthly cap unspent. Every feature in the product was
/// running on these strings, which is why they matter more than they look.
/// </summary>
public class AiFallbackHonestyTests
{
    private static string TextFallback(string feature)
    {
        var method = typeof(OpenRouterAiEngine).GetMethod(
            "GetDeterministicTextFallback", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var engine = (OpenRouterAiEngine)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(OpenRouterAiEngine));

        return (string)method!.Invoke(engine, [feature, "system", "user"])!;
    }


    /// <summary>
    /// The breakdown fallback used to return a complete seven-part reading of a paper nobody had
    /// read — "a statistically significant 31.4% improvement in operational efficacy", "published
    /// in verified peer-reviewed press with verifiable DOI" — attached to whichever real paper
    /// the reader had opened. A real title and a resolving DOI carrying invented findings is
    /// worse than an invented paper, because everything checkable about it checks out.
    /// </summary>
    [Fact]
    public void NoInventedPaperBreakdown()
    {
        // Read from source rather than invoked: the json fallback is generic, and reflecting on a
        // closed generic over an uninitialised instance tests the reflection more than the code.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RadarV2.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        // Comment lines are stripped first. The comment that replaced this branch quotes what it
        // used to return, which is the point of it — and a test that fails on its own
        // documentation would be removed rather than heeded.
        var source = string.Join(" ", File
            .ReadAllLines(Path.Combine(dir!.FullName, "Services", "OpenRouterAiEngine.cs"))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        string[] invented =
        [
            "31.4% improvement in operational efficacy",
            "verified peer-reviewed press with verifiable DOI",
            "robust error attenuation across stress scenarios",
            "multi-wave longitudinal modeling",
        ];

        var survivors = invented.Where(phrase => source.Contains(phrase, StringComparison.Ordinal)).ToList();

        Assert.Empty(survivors);
    }

    // Asking what a paper leaves open and being told Radar could not summarise it is an answer
    // to the other button.
    [Fact]
    public void GapsAndExplainDoNotShareAnAnswer()
    {
        var explain = TextFallback("research-explain");
        var gaps = TextFallback("research-gaps");

        Assert.NotEqual(explain, gaps);
        Assert.Contains("leaves open", gaps, StringComparison.OrdinalIgnoreCase);
    }

    // Every fallback has to read as a failure. The one this replaced — "X is a structured
    // methodology designed to solve complex operational challenges" — was true of nothing and
    // arrived in the same register as a real answer.
    [Theory]
    [InlineData("research-explain")]
    [InlineData("research-gaps")]
    [InlineData("something-new")]
    public void EveryTextFallbackSaysItFailed(string feature)
    {
        var text = TextFallback(feature);

        Assert.Contains("could not", text, StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan RetryDelay(int statusCode, string? errorBody, int attempt)
    {
        var method = typeof(OpenRouterAiEngine).GetMethod(
            "RetryDelayFor", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return (TimeSpan)method!.Invoke(null, [statusCode, errorBody, attempt])!;
    }

    /// <summary>
    /// An in-flight budget refusal resolves on its own; a quarter of a second does not help.
    ///
    /// OpenRouter answers 402 with reason "in_flight_budget_exhausted" when the requests already
    /// in flight have reserved the available credit. The enricher runs four at a time, so they
    /// exhaust it against each other, and the remedy OpenRouter itself gives is to retry once a
    /// sibling request settles. Measured on staging: these refusals arriving alongside successful
    /// calls on the same key.
    /// </summary>
    [Fact]
    public void AnInFlightRefusalWaitsLongEnoughToBeWorthRetrying()
    {
        const string inFlight =
            "{\"error\":{\"message\":\"This request would exceed your available credits given your "
            + "current in-flight requests. Retry after in-flight requests settle, or add credits.\","
            + "\"code\":402,\"metadata\":{\"reason\":\"in_flight_budget_exhausted\"}}}";

        var delay = RetryDelay(402, inFlight, 1);

        Assert.True(delay >= TimeSpan.FromSeconds(1), $"Backed off only {delay}.");
    }

    // Everything else keeps the short backoff: a rejected key is rejected just as fast a second
    // later, and making the reader wait for it helps nobody.
    [Fact]
    public void OtherRejectionsKeepTheShortBackoff()
    {
        var delay = RetryDelay(401, "{\"error\":{\"message\":\"User not found.\",\"code\":401}}", 1);

        Assert.True(delay < TimeSpan.FromSeconds(1), $"Backed off {delay} on a terminal error.");
    }

    [Fact]
    public void TheBackoffGrowsWithTheAttempt()
    {
        var first = RetryDelay(402, "in_flight_budget_exhausted", 1);
        var second = RetryDelay(402, "in_flight_budget_exhausted", 2);

        Assert.True(second > first);
    }

    /// <summary>
    /// 429 is the upstream provider rate-limiting us, and it was getting the terminal backoff.
    ///
    /// Found only because the text path started reporting its reason: a research-gaps call lost
    /// to "Provider returned error, code 429" from deepseek, in the same window as calls that
    /// succeeded. Before that commit the activity row read "Text generated via resilient brain"
    /// and this was invisible.
    /// </summary>
    [Fact]
    public void AProviderRateLimitWaitsLongEnoughToBeWorthRetrying()
    {
        var delay = RetryDelay(429, "{\"error\":{\"message\":\"Provider returned error\",\"code\":429}}", 1);

        Assert.True(delay >= TimeSpan.FromSeconds(1), $"Backed off only {delay} on a rate limit.");
    }
}
