using System.Security.Cryptography;

namespace RadarV2.Services;

/// <summary>
/// The code a participant shares, and what counts as having brought somebody.
/// </summary>
/// <remarks>
/// Two things are being rewarded and they are not the same: making something that explains
/// Radar, and actually getting people onto it. A good video that brings nobody should not
/// outrank somebody who introduced Radar to thirty people who went on to use it - so the two
/// are counted separately and never collapsed into one number.
///
/// This half is the measurable one. A code is attached at sign-up and never inferred: there is
/// no fingerprinting, no last-touch guessing from a header. Either the person arrived with a
/// code or they did not, and an unattributed sign-up stays unattributed rather than being
/// awarded to whoever seems most likely.
/// </remarks>
public static class ReferralCodes
{
    /// <summary>
    /// The alphabet a code is drawn from.
    /// </summary>
    /// <remarks>
    /// No O, 0, I, 1 or L. A referral code is read off a phone screen, written on a whiteboard
    /// at a campus society meeting and typed back in by somebody else - and the pairs that look
    /// alike in that chain cost a sign-up each time they are confused. Uppercase only, for the
    /// same reason: nobody has to remember whether it was capitalised.
    /// </remarks>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// How long a code is.
    /// </summary>
    /// <remarks>
    /// Seven characters of a thirty-one letter alphabet is about 27 billion codes, which is far
    /// more than needed and is not the point: the point is that it cannot be guessed. A short
    /// code would let somebody try codes until they landed on a real participant's and attribute
    /// their own sign-ups to a stranger, or inflate a rival's numbers with junk.
    /// </remarks>
    private const int Length = 7;

    /// <summary>A new code, from the system's cryptographic generator.</summary>
    public static string Generate()
    {
        var chars = new char[Length];

        for (var i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>
    /// A code as typed, in the form it is stored.
    /// </summary>
    /// <remarks>
    /// Somebody will paste "radar.app/join/ab3-k9z2 " with a trailing space, or type it in
    /// lower case, or include the hyphen a design put in for readability. All of those are the
    /// same code, and refusing them loses the sign-up the whole scheme exists to produce.
    /// Characters outside the alphabet are dropped rather than rejected.
    /// </remarks>
    public static string? Normalise(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // The last part of a link, when a link is what was pasted. Somebody sharing their code
        // sends the whole URL, and the person receiving it pastes the whole URL - "radar.app/
        // join/ABC2345" is the commonest form this arrives in, and reading only the final
        // segment is the difference between that working and being told the code is invalid.
        var segment = code.Trim().TrimEnd('/');
        var lastSlash = segment.LastIndexOf('/');
        if (lastSlash >= 0) segment = segment[(lastSlash + 1)..];

        var cleaned = new string([.. segment
            .ToUpperInvariant()
            .Where(Alphabet.Contains)]);

        return cleaned.Length == Length ? cleaned : null;
    }
}
