using System.Net;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Telling "spend less" apart from "there is nothing left".
/// </summary>
/// <remarks>
/// Both arrive as a 402, and the right response to each is the opposite of the other. OpenRouter
/// prices the reservation rather than the reply, so when the key cannot cover a request it often
/// names a smaller number it can - and asking again for that is a working request, which is why
/// the affordability retry exists.
///
/// When the balance is simply gone there is no such number, and retrying is doubling a doomed
/// call: two HTTP requests, two warnings and eight client log lines, across every enrichment at
/// once. That is what pushed the host past 500 logs a second and dropped 722 messages.
/// </remarks>
public class OutOfCreditTests
{
    /// <summary>The body production actually returned.</summary>
    private const string Exhausted =
        """
        {"error":{"message":"Insufficient credits. Add more using https://openrouter.ai/settings/credits","code":402,"metadata":{"limit_source":"openrouter_credits","remedy_hint":"Add credits at https://openrouter.ai/settings/credits, or lower max_tokens / prompt size to fit your remaining balance."}}}
        """;

    [Fact]
    public void AnExhaustedBalanceIsNotRetried() =>
        Assert.True(OpenRouterAiEngine.IsOutOfCredit(HttpStatusCode.PaymentRequired, Exhausted));

    /// <summary>
    /// A 402 that names an affordable size is not "out of credit".
    /// </summary>
    /// <remarks>
    /// Treating this one as terminal would be the costly mistake in the other direction - every
    /// model-backed feature going quiet over a number the provider just handed us. It has
    /// happened twice on this product, at 16384 tokens and at 1600.
    /// </remarks>
    [Fact]
    public void A402ThatNamesAnAffordableSizeIsStillWorthRetrying()
    {
        const string affordable =
            """{"error":{"message":"You requested up to 1600 tokens, but can only afford 1358","code":402}}""";

        Assert.False(OpenRouterAiEngine.IsOutOfCredit(HttpStatusCode.PaymentRequired, affordable));
        Assert.Equal(1358, OpenRouterAiEngine.AffordableTokens(affordable));
    }

    /// <summary>An affordable size too small to be worth asking for is still terminal.</summary>
    [Fact]
    public void AnAffordableSizeTooSmallToUseIsTerminal()
    {
        const string crumbs =
            """{"error":{"message":"You requested up to 1600 tokens, but can only afford 3","code":402}}""";

        Assert.True(OpenRouterAiEngine.IsOutOfCredit(HttpStatusCode.PaymentRequired, crumbs));
    }

    /// <summary>
    /// Only a 402 means this. Everything else keeps its retry.
    /// </summary>
    /// <remarks>
    /// 429 and the in-flight budget errors are the cases that genuinely clear on their own, and
    /// what has to happen is somebody else's request finishing.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public void OtherFailuresAreStillRetried(HttpStatusCode status) =>
        Assert.False(OpenRouterAiEngine.IsOutOfCredit(status, Exhausted));

    [Fact]
    public void AnEmptyBodyOnA402IsTreatedAsOutOfCredit() =>
        Assert.True(OpenRouterAiEngine.IsOutOfCredit(HttpStatusCode.PaymentRequired, null));
}
