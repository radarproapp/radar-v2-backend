using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace RadarV2.Services;

/// <summary>
/// Reading the paper itself, when the paper is free to read.
/// </summary>
/// <remarks>
/// The summary was written from the abstract, because the abstract was all Radar had. That put a
/// ceiling on it that no prompt could lift: an abstract states what was found often enough, and
/// almost never states how. Measured on production, a summary of a well-known paper came back
/// with "Not clearly stated in the available source." under both How they did it and What they
/// found - honest, and two of the five sections the spec asks for.
///
/// OpenAlex says, per paper, whether an open-access copy exists and where. That copy is published
/// to be read. Reading it is the only way to answer "how did they do it?" without inventing an
/// answer, which is the one thing this product will not do with academic work.
///
/// Nothing here follows a link a user supplied. The address comes from OpenAlex, and is still
/// checked: a hostname that resolves inside the network is refused, the download is capped, and
/// the request has a short deadline. A third party choosing an address is not the same as a safe
/// address.
/// </remarks>
public static class OpenAccessText
{
    /// <summary>How much of a file is worth downloading.</summary>
    /// <remarks>
    /// A journal PDF is a megabyte or two. Twelve is generous for a paper and small enough that a
    /// mis-typed link to something enormous costs a few seconds rather than the process.
    /// </remarks>
    public const int MostBytesWorthReading = 12 * 1024 * 1024;

    /// <summary>How long to wait for it.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How much of the extracted text to put in front of the model.
    /// </summary>
    /// <remarks>
    /// A paper runs to forty or fifty thousand characters and the summary needs five things out
    /// of it. Sending all of it costs tokens on pages - related work, references, appendices -
    /// that answer none of the five.
    /// </remarks>
    public const int MostCharactersWorthSending = 20000;

    /// <summary>
    /// Whether this is an address worth asking for.
    /// </summary>
    /// <remarks>
    /// Refuses anything that is not http or https, anything with a host that resolves to a
    /// loopback, private, link-local or otherwise internal address, and anything that fails to
    /// resolve at all. The address comes from OpenAlex rather than from a reader, so this is not
    /// the first line of defence - but a record in somebody else's database is not a thing to
    /// hand to an HTTP client unchecked, and "it came from our data provider" is exactly how an
    /// internal endpoint gets called by a trusted service.
    /// </remarks>
    public static bool IsWorthAsking(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        // A literal address is checked as it stands. A hostname is resolved, because a name that
        // points at 127.0.0.1 is the usual way of getting past a scheme check.
        if (IPAddress.TryParse(uri.Host, out var literal)) return !IsInternal(literal);

        try
        {
            var resolved = Dns.GetHostAddresses(uri.Host);
            return resolved.Length > 0 && resolved.All(address => !IsInternal(address));
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Whether this address belongs to the network Radar is running in.</summary>
    private static bool IsInternal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.Equals(IPAddress.IPv6Any);
        }

        var octets = address.GetAddressBytes();

        return octets[0] switch
        {
            0 => true,                                      // this network
            10 => true,                                     // private
            127 => true,                                    // loopback
            169 when octets[1] == 254 => true,              // link-local, where cloud metadata lives
            172 when octets[1] >= 16 && octets[1] <= 31 => true, // private
            192 when octets[1] == 168 => true,             // private
            _ => false,
        };
    }

    /// <summary>
    /// The part of a paper a summary is actually written from.
    /// </summary>
    /// <remarks>
    /// The opening, which carries the field and the problem, and then whatever the paper puts
    /// under its own method, results, discussion, limitation and conclusion headings. Those five
    /// headings are where the answers to the five questions live, and a paper that uses them says
    /// so in its own words.
    ///
    /// Taken from the start rather than sampled evenly: a paper truncated at twenty thousand
    /// characters usually loses its references and keeps its argument, but it can also lose the
    /// results, which is the section the abstract was already silent about. So the sections are
    /// found first and the opening fills whatever is left.
    /// </remarks>
    public static string WorthSummarising(string? paperText, int budget = MostCharactersWorthSending)
    {
        if (string.IsNullOrWhiteSpace(paperText)) return string.Empty;

        var text = Regex.Replace(paperText, @"[ \t]+", " ");
        text = Regex.Replace(text, @"(\r?\n){3,}", "\n\n").Trim();

        if (text.Length <= budget) return text;

        var taken = new StringBuilder();
        var sections = SectionsOf(text, budget / 2);

        // The opening gets whatever the named sections did not need, and at least a quarter of
        // the budget: a paper whose headings this cannot find still has to be summarisable.
        var openingBudget = Math.Max(budget / 4, budget - sections.Length);
        taken.Append(text[..Math.Min(openingBudget, text.Length)]);

        if (sections.Length > 0)
        {
            taken.AppendLine();
            taken.AppendLine();
            taken.Append(sections);
        }

        return taken.ToString();
    }

    /// <summary>The headings whose contents answer the questions a summary asks.</summary>
    private static readonly string[] Headings =
    [
        "method", "methodology", "materials and methods", "data and methods", "approach",
        "results", "findings", "discussion", "limitation", "conclusion",
    ];

    /// <summary>
    /// A slice of the paper from each named heading onwards, up to the budget.
    /// </summary>
    private static string SectionsOf(string text, int budget)
    {
        var slices = new List<string>();
        var spent = 0;
        var perSection = Math.Max(600, budget / Headings.Length);

        foreach (var heading in Headings)
        {
            if (spent >= budget) break;

            // At the start of a line, which is where a heading sits. Searching the whole text for
            // the word would match every sentence that mentions a method.
            var match = Regex.Match(
                text,
                $@"^\s*\d*\.?\s*{Regex.Escape(heading)}s?\b.*$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline,
                TimeSpan.FromMilliseconds(250));

            if (!match.Success) continue;

            var length = Math.Min(perSection, Math.Min(budget - spent, text.Length - match.Index));
            if (length <= 0) break;

            slices.Add(text.Substring(match.Index, length));
            spent += length;
        }

        return string.Join("\n\n", slices);
    }
}
