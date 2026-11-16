using System.Net;
using System.Text.Json;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Reads TalentApply's live job listings through the partner API.
/// </summary>
/// <remarks>
/// The first source Radar ingests under a signed agreement rather than an assumption, which makes
/// it the first whose redistribution rights are <see cref="Models.Redistribution.Permitted"/> and
/// the first whose descriptions may be stored.
///
/// Attribution rides on the listing's <c>view_url</c>: a candidate who clicks through and signs up
/// is credited to the partner account automatically. That makes the field load-bearing rather than
/// cosmetic - storing a bare job URL instead would silently drop the credit this integration
/// exists to earn, and nothing would fail to show it.
///
/// Everything here follows the published spec except the shape of a job itself, which TalentApply
/// publishes as an empty schema. See <see cref="TalentApplyJob"/>.
/// </remarks>
public interface ITalentApplyJobs
{
    /// <summary>Whether a key has been configured at all.</summary>
    bool IsConfigured { get; }

    /// <summary>One page of job listings, or null when the call could not be made.</summary>
    Task<TalentApplyPage?> FetchJobsAsync(
        IReadOnlyDictionary<string, string> filters, int page, CancellationToken ct);
}

/// <inheritdoc cref="ITalentApplyJobs"/>
/// <remarks>
/// The interface exists so the ingestion service can be tested. Without it the paging loop, the
/// per-query budget, the region tagging and the skip-an-unreadable-listing rule were reachable
/// only by making real HTTP calls, so none of them had a test - and those are exactly where the
/// review found the defects.
/// </remarks>
public class TalentApplyClient : ITalentApplyJobs
{
    /// <summary>
    /// A backstop on the whole run, well under their 120 a minute.
    /// </summary>
    /// <remarks>
    /// Sequential calls, so sixty of them take roughly half a minute and never approach the
    /// limiter even if a retry overlaps. This is the ceiling, not the plan: the per-query
    /// allowance below is what actually decides how much is read, and this only catches a source
    /// that reports far more pages than expected.
    /// </remarks>
    public const int MostRequestsPerRun = 170;

    /// <summary>
    /// How many pages any one query may take.
    /// </summary>
    /// <remarks>
    /// Per query rather than a pool shared across them. Shared and consumed in order, a query
    /// with enough pages took the entire budget and the ones after it never ran at all - so
    /// internships, Nigerian roles and remote work would go unread on any night graduate schemes
    /// happened to be deep, and the run would log as a success.
    ///
    /// Forty, raised from fifteen once the first production run reported what is actually there:
    /// 203 pages of internships, 91 of Nigerian roles, 60 of graduate schemes - about 17,650
    /// listings across three of the four queries. Fifteen pages was reading well under a tenth
    /// of it.
    ///
    /// Forty at fifty each is 2000 a query and 8000 across the four, which is a different
    /// proposition from 3000 and still short of everything. The limit beyond this is not the
    /// budget: paging restarts at page one every run with no cursor, so raising this reads more
    /// of the same early pages rather than reaching page 204. Covering a catalogue this size
    /// properly needs the ingester to remember where it stopped.
    /// </remarks>
    public const int MostPagesPerQuery = 40;

    /// <summary>
    /// The least time between two requests.
    /// </summary>
    /// <remarks>
    /// A real limiter, because there was none. The run was argued to be safe on the grounds that
    /// the first production pass took about eight seconds a page - but that is a measurement of
    /// TalentApply's server on one day, not a property of Radar. A page served from cache in a
    /// hundred milliseconds would have this loop firing ten a second, five times their limit,
    /// and the only thing incidentally slowing it down was the per-listing database round trip -
    /// which a page of unusable listings skips entirely.
    ///
    /// Six hundred milliseconds is a hundred a minute against their limit of 120, leaving room
    /// for anything else sharing the key. Across 160 requests it costs about ninety seconds, and
    /// it makes the budget self-limiting rather than dependent on how fast the source happens to
    /// answer.
    /// </remarks>
    public static readonly TimeSpan LeastTimeBetweenRequests = TimeSpan.FromMilliseconds(600);

    /// <summary>Their maximum page size, and the one that costs the fewest requests.</summary>
    public const int PageSize = 50;

    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TalentApplyClient> _log;

    public TalentApplyClient(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<TalentApplyClient> log)
    {
        _httpFactory = httpFactory;
        _config = config;
        _log = log;
    }

    private string? ApiKey => _config["TalentApply:ApiKey"];

    /// <summary>Whether a key has been configured at all.</summary>
    /// <remarks>
    /// Absent everywhere except production, so every caller has to cope with it being missing
    /// rather than throwing on a developer machine or in the test suite.
    /// </remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>One page of job listings, or null when the call could not be made.</summary>
    /// <remarks>
    /// Null rather than an empty page on failure, because the two mean different things to a
    /// paging loop: no results is a reason to stop, a failed request is not a reason to conclude
    /// the source is exhausted.
    /// </remarks>
    public Task<TalentApplyPage?> FetchJobsAsync(
        IReadOnlyDictionary<string, string> filters,
        int page,
        CancellationToken ct) => FetchJobsAsync(filters, page, ct, retrying: false);

    private async Task<TalentApplyPage?> FetchJobsAsync(
        IReadOnlyDictionary<string, string> filters,
        int page,
        CancellationToken ct,
        bool retrying)
    {
        if (!IsConfigured)
        {
            _log.LogDebug("TalentApply API key not configured — skipping.");
            return null;
        }

        var query = string.Join('&', filters
            .Append(new("page", page.ToString()))
            .Append(new("per_page", PageSize.ToString()))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"jobs?{query}");
        request.Headers.Authorization = new("Bearer", ApiKey);

        var http = _httpFactory.CreateClient("TalentApply");

        await PaceAsync(ct);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "TalentApply request failed for {Query}", query);
            return null;
        }

        using (response)
        {
            // Worth its own branch and its own severity. A revoked or mistyped key fails every
            // request for ever, and as a warning among transient network noise that reads as a
            // flaky source rather than something only a person can fix.
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.LogError(
                    "TalentApply rejected the API key (401). Ingestion from this source is stopped "
                    + "until TalentApply__ApiKey is corrected in the environment.");
                return null;
            }

            // Throttled, which is a wait rather than a failure. Treated as a generic error it
            // ended the query silently: one warning indistinguishable from a 500, a log line
            // saying "read 11 of 203 pages", and a run that reported success. Raising the page
            // budget made this more likely, not less.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = RetryAfter(response) ?? TimeSpan.FromSeconds(10);
                _log.LogWarning(
                    "TalentApply rate-limited the run; waiting {Seconds}s before one retry.",
                    wait.TotalSeconds);

                try
                {
                    await Task.Delay(wait, ct);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                return retrying ? null : await FetchJobsAsync(filters, page, ct, retrying: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    "TalentApply returned {Status} for {Query}", (int)response.StatusCode, query);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return Parse(body, _log);
        }
    }

    /// <summary>Waits long enough since the last request to stay inside their limit.</summary>
    /// <remarks>
    /// A single gate for the whole process rather than a counter per run, because two runs can
    /// overlap - the in-process timer and the deploy's cron - and their limit is per key, not per
    /// run. A semaphore makes the pacing hold across both.
    /// </remarks>
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastRequest = DateTime.MinValue;

    private static async Task PaceAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var since = DateTime.UtcNow - _lastRequest;
            if (since < LeastTimeBetweenRequests)
                await Task.Delay(LeastTimeBetweenRequests - since, ct);

            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>What the response asked us to wait, when it said.</summary>
    internal static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return wait;
        }

        // Capped rather than trusted. A source asking for an hour should not hold a nightly run
        // open for one, and the next run is never far away.
        return null;
    }

    /// <summary>
    /// Reads the documented envelope.
    /// </summary>
    /// <remarks>
    /// Internal so the parsing can be tested against recorded payloads without a key or a network.
    /// The envelope is the part of the spec that is complete, so it is parsed strictly: a response
    /// that does not carry data.items is a contract change, not an empty result, and returning
    /// null says so rather than reporting nothing found.
    /// </remarks>
    internal static TalentApplyPage? Parse(string body, ILogger log)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                log.LogWarning("TalentApply response had no data.items array.");
                return null;
            }

            var jobs = items
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new TalentApplyJob(item.Clone()))
                .ToList();

            var lastPage = 1;
            var total = jobs.Count;
            if (data.TryGetProperty("pagination", out var pagination))
            {
                // ValueKind first. TryGetInt32 does not return false for a non-number - it throws
                // InvalidOperationException - and only JsonException is caught here, so a
                // last_page arriving as "4" or null took down the whole run. That is exactly the
                // contract drift this envelope is parsed strictly to survive.
                if (pagination.TryGetProperty("last_page", out var last)
                    && last.ValueKind == JsonValueKind.Number
                    && last.TryGetInt32(out var parsedLast))
                    lastPage = parsedLast;

                // Reported so a run says how much the source actually holds. Without it the logs
                // could say how much Radar took and never how much was there, which is the number
                // that decides whether the budget is the binding constraint or the catalogue is.
                if (pagination.TryGetProperty("total", out var reported)
                    && reported.ValueKind == JsonValueKind.Number
                    && reported.TryGetInt32(out var parsedTotal))
                    total = parsedTotal;
            }

            return new TalentApplyPage(jobs, lastPage, total);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "TalentApply returned a body that was not JSON.");
            return null;
        }
    }
}

/// <summary>One page of results, and how many pages there are.</summary>
public sealed record TalentApplyPage(IReadOnlyList<TalentApplyJob> Jobs, int LastPage, int Total);
