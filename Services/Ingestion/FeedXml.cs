using System.Xml.Linq;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Reading a feed that is valid in every way that matters and invalid to a strict parser.
/// </summary>
/// <remarks>
/// An audit of all 250 content sources found 30 returning 200 with a body the XML reader refused.
/// Twenty of those thirty are real, current feeds: 99% Invisible with 842 episodes, Hard Fork
/// with 225, WHO with 83, three Conversation feeds with 50 each. They were being logged as
/// "fetch failed" and dropped, and the reported reasons were all the same shape -
///
///     Unexpected XML declaration. The XML declaration must be the first node in the document,
///     and no whitespace characters are allowed to appear before it. Line 2, position 3.
///
/// - which is a byte-order mark, a stray newline, or a blank line a CMS emitted before the
/// declaration. None of it changes what the feed says.
///
/// So the leading bytes are skipped and the document is parsed from its first real tag. This is
/// not lenient about content: anything that is not a feed after that still fails, and
/// <see cref="Parse"/> returns null rather than inventing a document.
/// </remarks>
public static class FeedXml
{
    /// <summary>
    /// Parses a feed body, or returns null when it genuinely is not one.
    /// </summary>
    /// <remarks>
    /// Null rather than throwing, because a source serving something unexpected is an ordinary
    /// event in a registry of 250 and should cost one log line, not an exception the caller has
    /// to wrap.
    /// </remarks>
    public static XDocument? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        foreach (var candidate in Candidates(body))
        {
            try
            {
                var document = Read(candidate);
                if (IsFeed(document)) return document;
            }
            catch (System.Xml.XmlException)
            {
                // Try the next starting point, then give up.
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a document with entity expansion turned off.
    /// </summary>
    /// <remarks>
    /// XDocument.Parse allows internal entities and caps expansion at ten million characters, so
    /// a four-hundred byte body nests ten entities six deep and arrives as a six-million
    /// character document - measured, not theorised. Eight feeds are fetched at once and the
    /// ingestion client sets no response size limit, and these are 250 URLs Radar does not
    /// control: one compromised publisher is enough.
    ///
    /// External entities were already inert - the default resolver is null, so no file read and
    /// no outbound request - and this changes nothing about that. It closes the internal case,
    /// which was open long before this file existed and is cheap to close now that every content
    /// and opportunity feed passes through one place.
    ///
    /// Prohibit throws XmlException, which the caller already catches, so a feed carrying a DTD
    /// is simply not read. No real RSS or Atom feed carries one.
    /// </remarks>
    private static XDocument Read(string body)
    {
        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using var reader = System.Xml.XmlReader.Create(new StringReader(body), settings);
        return XDocument.Load(reader);
    }

    /// <summary>
    /// Whether the document is a feed, rather than merely well-formed.
    /// </summary>
    /// <remarks>
    /// Parsing is not the question. A 404 page is often valid XML - "&lt;html&gt;&lt;body&gt;
    /// &lt;h1&gt;404 Not Found&lt;/h1&gt;&lt;/body&gt;&lt;/html&gt;" parses perfectly - so
    /// without this the method returned an HTML document to a caller that would read zero items
    /// from it and log nothing, which is the silent-empty-source failure this whole audit exists
    /// to end.
    ///
    /// Checked on the root element only, by local name, so a namespace Radar has not seen does
    /// not disqualify a real feed.
    /// </remarks>
    private static bool IsFeed(XDocument document)
    {
        var root = document.Root?.Name.LocalName;

        // Case-insensitively, because the first version compared ordinally and so rejected <RSS>
        // and <Feed> - which parsed and yielded items perfectly well before this check existed.
        // A hand-rolled or ASP-era CMS emits those, and the result was a new way to lose a source
        // inside the commit written to stop losing sources.
        //
        // "channel" too: a bare <channel> document with no <rss> wrapper is malformed by the spec
        // and is still a feed with items in it, which is the only question being asked here.
        return root is not null
            && (root.Equals("rss", StringComparison.OrdinalIgnoreCase)
                || root.Equals("feed", StringComparison.OrdinalIgnoreCase)
                || root.Equals("RDF", StringComparison.OrdinalIgnoreCase)
                || root.Equals("channel", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The body as given, then from its declaration, then from its first element.
    /// </summary>
    /// <remarks>
    /// In that order so a well-formed document is parsed exactly as before and pays nothing for
    /// this. Only a body that has already failed is retried, and each retry drops strictly more
    /// of the leading junk - so the first success is the one closest to what the publisher sent.
    /// </remarks>
    private static IEnumerable<string> Candidates(string body)
    {
        yield return body;

        var trimmed = body.TrimStart('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');
        // TrimStart returns the same instance when it trims nothing, so the length check
        // alone says everything the reference check did.
        if (trimmed.Length != body.Length) yield return trimmed;

        foreach (var opening in new[] { "<?xml", "<rss", "<feed", "<rdf:RDF" })
        {
            var at = trimmed.IndexOf(opening, StringComparison.OrdinalIgnoreCase);

            // Only skip a preamble, never hunt for a tag deep inside an HTML page: a document
            // whose feed element is three kilobytes in is a web page that mentions one.
            if (at > 0 && at < 2048) yield return trimmed[at..];
        }
    }
}
