using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RadarV2.Services;

/// <summary>
/// Somewhere to put bytes that do not belong in Mongo.
/// </summary>
/// <remarks>
/// Cloudflare R2, which speaks S3. Signed by hand with SigV4 rather than taking the AWS SDK: the
/// SDK is a large dependency for four operations, and R2 charges nothing for egress, which is the
/// reason to use it over S3 in the first place.
///
/// <para>
/// What this is for, in the order the need is real:
/// </para>
///
/// <list type="number">
/// <item><b>Transcripts.</b> A forty-minute episode is tens of kilobytes of text. Thousands of
/// those in Mongo documents makes every feed query drag a payload it never reads; in object
/// storage they cost almost nothing and are fetched only when opened.</item>
/// <item><b>Audio the user recorded.</b> There is already an endpoint that takes a browser
/// recording and transcribes it, and the audio is then thrown away. It is the user's own
/// recording, so keeping it is uncomplicated and useful.</item>
/// <item><b>Uploaded documents.</b> An assessment currently reads the file and discards it, so
/// "compare this against my last draft" cannot be built.</item>
/// </list>
///
/// <para>
/// Not for re-hosting podcast audio from a feed. A podcast RSS enclosure is published for players
/// to fetch from the publisher; copying it onto Radar's own domain and serving it from there is a
/// different act, and most licences do not cover it. Worth saying plainly because the useful half
/// of "make podcasts searchable" is the transcript, not the audio — text is what a search matches
/// against, and a transcript is a derived work with a far better footing than a copy of the file.
/// </para>
/// </remarks>
public interface IObjectStore
{
    bool IsConfigured { get; }

    /// <summary>Stores bytes at a key, overwriting. Returns false when it could not.</summary>
    Task<bool> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Reads bytes back, or null when the key is absent.</summary>
    Task<Stream?> GetAsync(string key, CancellationToken ct = default);

    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// A URL that grants temporary read access without the caller holding credentials.
    /// </summary>
    /// <remarks>
    /// Needed because the bucket is private. Making it public to avoid signing would mean every
    /// stored transcript and every uploaded CV was readable by anybody who guessed a key.
    /// </remarks>
    string? SignedUrl(string key, TimeSpan validFor);
}

public class R2ObjectStore : IObjectStore
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<R2ObjectStore> _log;

    public R2ObjectStore(IConfiguration config, IHttpClientFactory httpFactory, ILogger<R2ObjectStore> log)
    {
        _config = config;
        _httpFactory = httpFactory;
        _log = log;
    }

    private string? AccountId => _config["R2:AccountId"];
    private string? AccessKey => _config["R2:AccessKeyId"];
    private string? Secret => _config["R2:SecretAccessKey"];
    private string? Bucket => _config["R2:Bucket"];

    /// <summary>
    /// All four settings, not just one.
    /// </summary>
    /// <remarks>
    /// The same mistake the OpenRouter status field made: reporting live because a key string was
    /// present, while every call failed. A store missing its bucket name is not configured.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(Secret)
        && !string.IsNullOrWhiteSpace(Bucket);

    /// <summary>R2 is single-region and expects this literal.</summary>
    private const string Region = "auto";
    private const string Service = "s3";

    private string Endpoint => $"https://{AccountId}.r2.cloudflarestorage.com";

    public async Task<bool> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            // Said rather than swallowed. A silent false here is how "nothing is being stored"
            // becomes indistinguishable from "storage is broken".
            _log.LogInformation("Object store not configured; refusing to store {Key}.", key);
            return false;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        try
        {
            using var request = Signed(HttpMethod.Put, key, bytes, contentType);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.Add("Content-Type", contentType);

            using var response = await _httpFactory.CreateClient("R2").SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;

            _log.LogWarning(
                "R2 rejected a put of {Key}: HTTP {Status}. {Body}",
                key,
                (int)response.StatusCode,
                await response.Content.ReadAsStringAsync(ct));

            return false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not store {Key} in R2.", key);
            return false;
        }
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;

        try
        {
            using var request = Signed(HttpMethod.Get, key, [], null);
            var response = await _httpFactory.CreateClient("R2").SendAsync(request, ct);

            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsStreamAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read {Key} from R2.", key);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        try
        {
            using var request = Signed(HttpMethod.Delete, key, [], null);
            using var response = await _httpFactory.CreateClient("R2").SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not delete {Key} from R2.", key);
            return false;
        }
    }

    /// <summary>
    /// A presigned GET, with the signature in the query string rather than a header.
    /// </summary>
    /// <remarks>
    /// Query signing is what lets the URL be handed to a browser, an audio element or an img tag,
    /// none of which can attach an Authorization header.
    /// </remarks>
    public string? SignedUrl(string key, TimeSpan validFor)
    {
        if (!IsConfigured) return null;

        var now = DateTime.UtcNow;
        var stamp = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = $"{date}/{Region}/{Service}/aws4_request";
        var path = $"/{Bucket}/{Uri.EscapeDataString(key).Replace("%2F", "/")}";

        var expires = ((int)Math.Clamp(validFor.TotalSeconds, 1, 604800)).ToString(CultureInfo.InvariantCulture);

        // Canonical query: sorted, and every value escaped. Getting the order wrong is the usual
        // cause of a signature that looks right and is rejected.
        var query = string.Join("&",
        [
            "X-Amz-Algorithm=AWS4-HMAC-SHA256",
            $"X-Amz-Credential={Uri.EscapeDataString($"{AccessKey}/{scope}")}",
            $"X-Amz-Date={stamp}",
            $"X-Amz-Expires={expires}",
            "X-Amz-SignedHeaders=host",
        ]);

        var host = new Uri(Endpoint).Host;

        var canonical = string.Join("\n",
            "GET", path, query, $"host:{host}\n", "host", "UNSIGNED-PAYLOAD");

        var toSign = string.Join("\n", "AWS4-HMAC-SHA256", stamp, scope, Hex(Sha256(canonical)));
        var signature = Hex(HmacSha256(SigningKey(date), toSign));

        return $"{Endpoint}{path}?{query}&X-Amz-Signature={signature}";
    }

    /// <summary>A request signed with SigV4 in the Authorization header.</summary>
    private HttpRequestMessage Signed(HttpMethod method, string key, byte[] payload, string? contentType)
    {
        var now = DateTime.UtcNow;
        var stamp = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = $"{date}/{Region}/{Service}/aws4_request";

        var path = $"/{Bucket}/{Uri.EscapeDataString(key).Replace("%2F", "/")}";
        var host = new Uri(Endpoint).Host;
        var payloadHash = Hex(Sha256(payload));

        var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{stamp}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";

        var canonical = string.Join("\n",
            method.Method, path, string.Empty, canonicalHeaders, signedHeaders, payloadHash);

        var toSign = string.Join("\n", "AWS4-HMAC-SHA256", stamp, scope, Hex(Sha256(canonical)));
        var signature = Hex(HmacSha256(SigningKey(date), toSign));

        var request = new HttpRequestMessage(method, $"{Endpoint}{path}");
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", stamp);
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"AWS4-HMAC-SHA256 Credential={AccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");

        if (contentType is not null)
            request.Headers.TryAddWithoutValidation("x-amz-meta-content-type", contentType);

        return request;
    }

    /// <summary>The four-step derived key SigV4 requires.</summary>
    private byte[] SigningKey(string date)
    {
        var key = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{Secret}"), date);
        key = HmacSha256(key, Region);
        key = HmacSha256(key, Service);
        return HmacSha256(key, "aws4_request");
    }

    private static byte[] Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));

    private static byte[] Sha256(byte[] value) => SHA256.HashData(value);

    private static byte[] HmacSha256(byte[] key, string value) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
