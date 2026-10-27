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
}
