using System.Text.Json;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Lightweight HTTP client for the OpenAlex REST API (https://api.openalex.org/works).
/// Free tier: no API key required. Handles search, single-work fetch, and related works.
/// </summary>
public class OpenAlexClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public OpenAlexClient(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("OpenAlex");
    }

    /// <summary>
    /// Search for works (papers) by query string, optionally filtered by publication year.
    /// Returns up to <paramref name="pageSize"/> results from <paramref name="page"/>.
    /// </summary>
    public async Task<List<ContentItem>> SearchAsync(string query, int yearFrom = 0, int page = 1, int pageSize = 20)
    {
        try
        {
            var filters = new List<string>();
            if (yearFrom > 0)
                filters.Add($"publication_year:{yearFrom}-");

            var url = $"https://api.openalex.org/works?search={Uri.EscapeDataString(query)}&per_page={Math.Min(pageSize, 100)}&page={page}";
            if (filters.Count > 0)
                url += $"&filter={string.Join(",", filters)}";

            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new ResearchUnavailableException(
                    $"The research index answered {(int)response.StatusCode}. Your search is fine - try it again shortly.");

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            // No "results" key at all is a malformed answer, not an empty one. An empty array
            // is an empty answer, and falls through to the loop and an empty list - which is
            // the one case where "nothing matches your search" is the truth.
            if (ArrayProperty(doc.RootElement, "results") is not { } results)
                throw new ResearchUnavailableException(
                    "The research index answered with something Radar could not read. Try again shortly.");

            var items = new List<ContentItem>();
            foreach (var work in results.EnumerateArray())
            {
                if (work.ValueKind != JsonValueKind.Object) continue;
                items.Add(MapWorkToContentItem(work));
            }
            return items;
        }
        catch (Exception e) when (e is not ResearchUnavailableException)
        {
            // Said, not swallowed.
            //
            // This caught everything and returned an empty list, so a timeout, a 429 and a
            // rate-limited 503 all arrived at the reader as "nothing matches your search" -
            // an outage reported as a fact about their topic, with no retry offered because
            // nothing had failed as far as the screen knew. It also made the whole
            // ResearchUnavailableException path above it unreachable: the exception could not
            // be thrown, so the controller could not return 503, so the dead code sat there
            // looking like a fix.
            throw new ResearchUnavailableException(
                "Radar could not reach the research index just now. Your search is fine - try it again shortly.", e);
        }
    }

    /// <summary>
    /// Fetch a single work by its OpenAlex ID (e.g. "W2741809807").
    /// </summary>
    public async Task<ContentItem?> GetWorkByIdAsync(string openAlexId)
    {
        var url = $"https://api.openalex.org/works/{openAlexId}";
        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return MapWorkToContentItem(doc.RootElement);
    }

    /// <summary>
    /// Fetch related works by topic, using the same topics as the given work.
    /// </summary>
    public async Task<List<ContentItem>> GetRelatedWorksAsync(string openAlexId, int limit = 5)
    {
        // First get the work to find its topics
        var work = await GetWorkByIdAsync(openAlexId);
        if (work is null) return [];

        // Use the work's topics to find related works
        var topicFilter = work.Tags.Count > 0
            ? $"&filter=topics.display_name:{Uri.EscapeDataString(work.Tags.First())}"
            : "";

        var url = $"https://api.openalex.org/works?per_page={limit}&sort=cited_by_count:desc{topicFilter}";
        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (ArrayProperty(doc.RootElement, "results") is not { } results)
            return [];

        var items = new List<ContentItem>();
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var mapped = MapWorkToContentItem(item);
            if (mapped.Id != openAlexId)
                items.Add(mapped);
        }
        return items.Take(limit).ToList();
    }

    /// <summary>
    /// Generate a formatted citation for a paper in APA, Harvard, IEEE, BibTeX, MLA, or Chicago style.
    /// </summary>
    public static string FormatCitation(ContentItem paper, string style = "APA")
    {
        return AcademicPaperSynthesizer.FormatCitation(paper, style);
    }

    private static ContentItem MapWorkToContentItem(JsonElement work)
    {
        var id = work.TryGetProperty("id", out var idEl)
            ? idEl.GetString()?.Replace("https://openalex.org/", "") ?? ""
            : "";

        var title = work.TryGetProperty("title", out var titleEl)
            ? titleEl.GetString() ?? ""
            : "";

        var doi = work.TryGetProperty("doi", out var doiEl)
            ? doiEl.GetString()?.Replace("https://doi.org/", "") ?? ""
            : "";

        var citedByCount = work.TryGetProperty("cited_by_count", out var citeEl)
            ? citeEl.GetInt32()
            : 0;

        // default, not today. A work whose date OpenAlex does not report was being stamped with
        // the moment Radar happened to read it - so a paper of unknown age rendered as this
        // morning, which is an invented publication date, and the spec forbids exactly that.
        // Everything downstream now has a value it can recognise as "not recorded".
        var publicationDate = work.TryGetProperty("publication_date", out var dateEl)
            && DateTime.TryParse(dateEl.GetString(), out var dt)
            ? dt
            : default;

        var journal = "";
        string? landingPageUrl = null;
        string? pdfUrl = null;

        if (ObjectProperty(work, "primary_location") is { } loc)
        {
            if (ObjectProperty(loc, "source") is { } source &&
                source.TryGetProperty("display_name", out var srcName))
            {
                journal = srcName.GetString() ?? "";
            }

            if (loc.TryGetProperty("landing_page_url", out var landEl))
                landingPageUrl = landEl.GetString();

            if (loc.TryGetProperty("pdf_url", out var pdfEl))
                pdfUrl = pdfEl.GetString();
        }

        if (ObjectProperty(work, "best_oa_location") is { } bestOa)
        {
            if (string.IsNullOrEmpty(landingPageUrl) && bestOa.TryGetProperty("landing_page_url", out var bLandEl))
                landingPageUrl = bLandEl.GetString();

            if (string.IsNullOrEmpty(pdfUrl) && bestOa.TryGetProperty("pdf_url", out var bPdfEl))
                pdfUrl = bPdfEl.GetString();
        }

        // Determine verified paper link
        var paperUrl = !string.IsNullOrWhiteSpace(doi)
            ? (doi.StartsWith("http") ? doi : $"https://doi.org/{doi}")
            : (!string.IsNullOrWhiteSpace(landingPageUrl) ? landingPageUrl : $"https://openalex.org/{id}");

        // Map document type
        var rawType = work.TryGetProperty("type", out var typeEl) ? typeEl.GetString()?.ToLowerInvariant() : "";
        var docType = rawType switch
        {
            "proceedings-article" => "Conference Paper",
            "book-chapter" => "Book Chapter",
            "review" => "Review Article",
            "preprint" => "Preprint",
            _ => "Journal Article"
        };

        // Extract authors from authorships
        var authors = new List<string>();
        if (ArrayProperty(work, "authorships") is { } authorships)
        {
            foreach (var authorship in authorships.EnumerateArray())
            {
                if (ObjectProperty(authorship, "author") is { } author &&
                    author.TryGetProperty("display_name", out var name))
                {
                    authors.Add(name.GetString() ?? "");
                }
            }
        }

        // Extract topics
        var tags = new List<string>();
        if (ArrayProperty(work, "topics") is { } topics)
        {
            foreach (var topic in topics.EnumerateArray())
            {
                if (topic.ValueKind == JsonValueKind.Object &&
                    topic.TryGetProperty("display_name", out var topicName))
                {
                    tags.Add(topicName.GetString() ?? "");
                }
            }
        }

        // Abstract from inverted index (OpenAlex stores abstracts as inverted index)
        var abstractText = "";
        if (ObjectProperty(work, "abstract_inverted_index") is { } absIndex)
        {
            abstractText = ReconstructAbstract(absIndex);
        }

        var item = new ContentItem
        {
            Id = id,
            Type = ContentType.ResearchPaper,
            Title = title,
            Doi = doi,
            Url = paperUrl,
            OpenAccessPdfUrl = pdfUrl,
            DocumentType = docType,
            // OpenAlex. CrossRef is never queried, and naming it was a claim about where this
            // record came from that happened not to be true.
            DatabaseSource = "OpenAlex",
            Authors = authors,
            // The venue, or nothing. "Academic Press" was the fallback: a real Elsevier imprint,
            // named as the publisher of any record OpenAlex returns no venue for - preprints,
            // dissertations, datasets. It flowed into the exported APA, Harvard, MLA and Chicago
            // citations, so a student pasted a false publisher into a reference list. The same
            // invented string was removed from the synthesizer and survived here.
            Source = !string.IsNullOrWhiteSpace(journal) ? journal : string.Empty,
            Journal = journal,
            PublishedAt = publicationDate,
            AiSummary = abstractText.Length > 500 ? abstractText[..500] + "…" : abstractText,
            Tags = tags,
            Signal = title,
            Topic = tags.Count > 0 ? tags[0] : "Academic Research",
            WhatHappened = abstractText,
            // Citations, and the venue only when there is one. This read "Cited 0 times in
            // peer-reviewed scholarship. Published in ." for a preprint with no venue - two
            // false claims and a dangling sentence, on every research response.
            WhyItMatters = string.IsNullOrWhiteSpace(journal)
                ? $"Cited {citedByCount} times."
                : $"Cited {citedByCount} times. Published in {journal}.",

            // Kept as a number as well as a sentence. It is the one structured measure of
            // standing the source gives us, and ranking cannot read it out of prose.
            CitationCount = citedByCount,
        };

        // No summary attached here, deliberately.
        //
        // Every mapped work used to get the template breakdown - abstract sentences sorted into
        // headings by keyword - and that is what made the plain-English summary unreachable. The
        // card only fetched one when the paper did not already carry one, so it never fetched:
        // search results arrived carrying the template, and the template is what every reader
        // saw. Measured on production, where the search response had a breakdown on it and the
        // same paper fetched by id came back in plain English nobody could get to.
        //
        // A list row does not render a summary, so building one per result was work nobody read
        // even when it was right.
        return item;
    }

    /// <summary>
    /// Reads a nested property only when the parent really is a JSON object. OpenAlex returns an
    /// explicit null for missing nested objects, and reading through that throws
    /// InvalidOperationException instead of returning false — which used to 500 the search API.
    /// </summary>
    private static JsonElement? ObjectProperty(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    /// <summary>Same guard as <see cref="ObjectProperty"/>, for arrays (absent or null).</summary>
    private static JsonElement? ArrayProperty(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? value
            : null;

    /// <summary>
    /// Reconstruct readable text from OpenAlex's inverted index format.
    /// The inverted index maps words to their positions in the abstract.
    /// </summary>
    private static string ReconstructAbstract(JsonElement invertedIndex)
    {
        var wordPositions = new List<(string word, int position)>();

        foreach (var prop in invertedIndex.EnumerateObject())
        {
            var word = prop.Name;
            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var pos in prop.Value.EnumerateArray())
                {
                    if (pos.TryGetInt32(out var p))
                        wordPositions.Add((word, p));
                }
            }
        }

        wordPositions.Sort((a, b) => a.position.CompareTo(b.position));
        return string.Join(" ", wordPositions.Select(wp => wp.word));
    }
}
