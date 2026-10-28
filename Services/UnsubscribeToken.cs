using System.Security.Cryptography;
using System.Text;

namespace RadarV2.Services;

/// <summary>
/// The token in an unsubscribe link.
/// </summary>
/// <remarks>
/// An unsubscribe link has to work for somebody who is not signed in, on a device that has never
/// been signed in, months after the message arrived. That rules out a session, and it rules out
/// an expiry: a link that stops working is a user who cannot opt out, which is the complaint that
/// gets a sending domain blocked.
///
/// So it is a signed assertion rather than a stored row: an HMAC over the user id and the kind of
/// message. Nothing to look up, nothing to expire, and it cannot be forged or walked — changing
/// the id invalidates the signature, so there is no way to unsubscribe somebody else.
///
/// The kind is in the signature on purpose. A link to stop the daily digest must not also be able
/// to stop a password reset.
/// </remarks>
public static class UnsubscribeToken
{
    /// <summary>Which stream the link switches off.</summary>
    public const string DailyDigest = "daily";

    public static string Create(string userId, string kind, string signingKey)
    {
        var payload = $"{userId}:{kind}";
        var signature = Sign(payload, signingKey);

        // Base64url so the token survives being a query parameter, an href in HTML, and a line
        // wrap in a plain-text part — all three of which mangle raw base64.
        return $"{Base64Url(Encoding.UTF8.GetBytes(payload))}.{signature}";
    }

    /// <summary>
    /// Reads a token back. Returns null for anything that does not verify.
    /// </summary>
    /// <remarks>
    /// One null for a malformed token, a wrong signature and an unknown kind alike. A caller that
    /// could tell them apart would be an oracle for which user ids exist.
    /// </remarks>
    public static (string UserId, string Kind)? Read(string? token, string signingKey)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
        }
        catch
        {
            return null;
        }

        // Fixed-time comparison. A length-dependent one leaks how much of a guessed signature was
        // right, which is enough to build a valid one a byte at a time.
        var expected = Sign(payload, signingKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1])))
        {
            return null;
        }

        var split = payload.LastIndexOf(':');
        if (split <= 0) return null;

        return (payload[..split], payload[(split + 1)..]);
    }

    private static string Sign(string payload, string signingKey) =>
        Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(payload)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
