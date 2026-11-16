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
public class TalentApplyClient
{
    /// <summary>Their cap is 120 requests a minute; this stays far enough below it to share.</summary>
    public const int MostRequestsPerRun = 20;

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
    public async Task<TalentApplyPage?> FetchJobsAsync(
        IReadOnlyDictionary<string, string> filters,
        int page,
        CancellationToken ct)
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
            if (data.TryGetProperty("pagination", out var pagination)
                && pagination.TryGetProperty("last_page", out var last)
                && last.TryGetInt32(out var parsed))
            {
                lastPage = parsed;
            }

            return new TalentApplyPage(jobs, lastPage);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "TalentApply returned a body that was not JSON.");
            return null;
        }
    }
}

/// <summary>One page of results, and how many pages there are.</summary>
public sealed record TalentApplyPage(IReadOnlyList<TalentApplyJob> Jobs, int LastPage);
