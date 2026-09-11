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
        var filters = new List<string>();
        if (yearFrom > 0)
            filters.Add($"publication_year:{yearFrom}-");

        var url = $"https://api.openalex.org/works?search={Uri.EscapeDataString(query)}&per_page={Math.Min(pageSize, 100)}&page={page}";
        if (filters.Count > 0)
            url += $"&filter={string.Join(",", filters)}";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return [];

        var items = new List<ContentItem>();
        foreach (var work in results.EnumerateArray())
        {
            items.Add(MapWorkToContentItem(work));
        }
        return items;
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

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return [];

        var items = new List<ContentItem>();
        foreach (var item in results.EnumerateArray())
        {
            var mapped = MapWorkToContentItem(item);
            if (mapped.Id != openAlexId)
                items.Add(mapped);
        }
        return items.Take(limit).ToList();
    }

    /// <summary>
    /// Generate an APA or MLA citation for a work.
    /// </summary>
    public static string FormatCitation(ContentItem paper, string style = "APA")
    {
        var authors = paper.Authors.Count > 0
            ? string.Join(", ", paper.Authors)
            : "Unknown";

        var year = paper.PublishedAt.Year;
        var title = paper.Title;
        var journal = paper.Journal ?? paper.Source;
        var doi = paper.Doi;

        return style.ToUpperInvariant() switch
        {
            "APA" => $"{authors} ({year}). {title}. {journal}. https://doi.org/{doi}",
            "MLA" => $"{authors}. \"{title}.\" {journal}, {year}. https://doi.org/{doi}.",
            _ => $"{authors} ({year}). {title}. {journal}. https://doi.org/{doi}",
        };
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

        var publicationDate = work.TryGetProperty("publication_date", out var dateEl)
            ? DateTime.TryParse(dateEl.GetString(), out var dt) ? dt : DateTime.UtcNow
            : DateTime.UtcNow;

        var journal = "";
        if (work.TryGetProperty("primary_location", out var loc) &&
            loc.TryGetProperty("source", out var source) &&
            source.TryGetProperty("display_name", out var srcName))
        {
            journal = srcName.GetString() ?? "";
        }

        // Extract authors from authorships
        var authors = new List<string>();
        if (work.TryGetProperty("authorships", out var authorships))
        {
            foreach (var authorship in authorships.EnumerateArray())
            {
                if (authorship.TryGetProperty("author", out var author) &&
                    author.TryGetProperty("display_name", out var name))
                {
                    authors.Add(name.GetString() ?? "");
                }
            }
        }

        // Extract topics
        var tags = new List<string>();
        if (work.TryGetProperty("topics", out var topics))
        {
            foreach (var topic in topics.EnumerateArray())
            {
                if (topic.TryGetProperty("display_name", out var topicName))
                {
                    tags.Add(topicName.GetString() ?? "");
                }
            }
        }

        // Abstract from inverted index (OpenAlex stores abstracts as inverted index)
        var abstractText = "";
        if (work.TryGetProperty("abstract_inverted_index", out var absIndex) &&
            absIndex.ValueKind == JsonValueKind.Object)
        {
            abstractText = ReconstructAbstract(absIndex);
        }

        return new ContentItem
        {
            Id = id,
            Type = ContentType.ResearchPaper,
            Title = title,
            Doi = doi,
            Authors = authors,
            Source = journal,
            Journal = journal,
            PublishedAt = publicationDate,
            AiSummary = abstractText.Length > 500 ? abstractText[..500] + "…" : abstractText,
            Tags = tags,
            Signal = title,
            WhatHappened = abstractText,
            WhyItMatters = $"Cited {citedByCount} times. Published in {journal}.",
        };
    }

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
