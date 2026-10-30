using System.Net;
using RadarV2.Services;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// An outage is not a finding about the reader's topic.
/// </summary>
/// <remarks>
/// The client caught everything and returned an empty list, so a timeout, a 429 and a
/// rate-limited 503 all reached the reader as "Nothing matches your search" - with no retry
/// offered, because as far as the screen knew nothing had failed. A reader then believes no
/// published work exists on what they asked about.
///
/// It was reported as fixed once already and was not: the service above it was given a throw
/// and the controller a 503, while the client below went on swallowing everything, so the
/// exception could not be raised and the whole path was unreachable. Which is why this test
/// exists at the layer that actually decides.
/// </remarks>
public class ResearchUnavailableTests
{
    private static OpenAlexClient ClientThat(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(new OneClientFactory(new HttpClient(new Answering(answer))));

    private static OpenAlexClient ClientThatThrows() =>
        new(new OneClientFactory(new HttpClient(new Throwing())));

    [Fact]
    public async Task AnIndexThatCannotBeReachedIsSaidOutLoud()
    {
        var client = ClientThatThrows();

        var failure = await Assert.ThrowsAsync<ResearchUnavailableException>(
            () => client.SearchAsync("malaria"));

        Assert.Contains("try it again", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnIndexThatRefusesIsNotReportedAsAnEmptyShelf(HttpStatusCode status)
    {
        var client = ClientThat(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<ResearchUnavailableException>(() => client.SearchAsync("malaria"));
    }

    /// <summary>
    /// And a genuinely empty answer is still empty. This is the one case where "nothing matches
    /// your search" is the truth, and it has to stay distinguishable from the ones above.
    /// </summary>
    [Fact]
    public async Task NoResultsIsAnEmptyListRatherThanAFailure()
    {
        var client = ClientThat(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"results":[]}"""),
        });

        Assert.Empty(await client.SearchAsync("a subject nobody has written about"));
    }

    /// <summary>An answer Radar cannot read is a failure, not an empty shelf.</summary>
    [Fact]
    public async Task AnAnswerWithNoResultsKeyIsAFailure()
    {
        var client = ClientThat(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"meta":{"count":0}}"""),
        });

        await Assert.ThrowsAsync<ResearchUnavailableException>(() => client.SearchAsync("malaria"));
    }

    private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(answer(request));
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("the index is not answering");
    }

    private sealed class OneClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
