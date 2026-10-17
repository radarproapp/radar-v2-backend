using System.Text.RegularExpressions;

namespace RadarV2.Helpers;

public sealed class DocumentSection
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public string Content { get; set; } = string.Empty;
}

public static class DocumentSectionDetector
{
    // Common chapter/section heading patterns
    private static readonly Regex[] HeadingPatterns =
    [
        // Chapter 1: Introduction / Chapter IV / Section 2.1 / Part I / Module 3
        new(@"^[ \t]*(?:chapter|section|part|module|unit)\s+([0-9ivxlcdm]+|\w+)[:.\s\-–—]+([^\r\n]{2,90})", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled),
        
        // Markdown headers: # Heading, ## Section
        new(@"^#{1,3}\s+([^\r\n]{3,90})", RegexOptions.Multiline | RegexOptions.Compiled),

        // Numbered sections: 1. Executive Summary, 2.0 Market Sizing, 3.1 Financial Plan
        new(@"^[ \t]*([0-9]{1,2}(?:\.[0-9]{1,2}){0,2})\.?\s+([A-Z][^\r\n]{2,80})", RegexOptions.Multiline | RegexOptions.Compiled),

        // Roman numerals: I. Introduction, II. Methodology
        new(@"^[ \t]*([IVXLCDM]{1,6})\.\s+([A-Z][^\r\n]{2,80})", RegexOptions.Multiline | RegexOptions.Compiled),
    ];

    public static List<DocumentSection> DetectSections(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 300)
            return [];

        // Try each pattern in priority order
        foreach (var pattern in HeadingPatterns)
        {
            var matches = pattern.Matches(text);
            if (matches.Count < 2) continue;

            var candidates = new List<(string Title, int Index)>();
            foreach (Match m in matches)
            {
                var title = m.Value.Trim();
                // Clean markdown hashes if present
                title = Regex.Replace(title, @"^#+\s*", "");
                title = Regex.Replace(title, @"\s+", " ").Trim();

                if (title.Length >= 3 && title.Length <= 100)
                {
                    candidates.Add((title, m.Index));
                }
            }

            // Filter out Table of Contents clusters where headings are clumped within 120 chars
            var substantialSections = new List<(string Title, int Index)>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var current = candidates[i];
                var nextIndex = (i + 1 < candidates.Count) ? candidates[i + 1].Index : text.Length;
                var bodyLength = nextIndex - current.Index;

                // Substantive section has at least 150 characters of content
                if (bodyLength >= 150)
                {
                    substantialSections.Add(current);
                }
            }

            // If we found between 2 and 25 substantive sections, construct the section breakdown
            if (substantialSections.Count >= 2 && substantialSections.Count <= 25)
            {
                var result = new List<DocumentSection>();
                for (int i = 0; i < substantialSections.Count; i++)
                {
                    var start = substantialSections[i].Index;
                    var end = (i + 1 < substantialSections.Count) ? substantialSections[i + 1].Index : text.Length;
                    var sectionText = text.Substring(start, end - start).Trim();

                    result.Add(new DocumentSection
                    {
                        Number = i + 1,
                        Title = substantialSections[i].Title,
                        StartIndex = start,
                        EndIndex = end,
                        Content = sectionText
                    });
                }

                return result;
            }
        }

        return [];
    }

    public static string? ExtractSelectedSectionContent(List<DocumentSection> sections, string sectionQuery)
    {
        if (sections.Count == 0 || string.IsNullOrWhiteSpace(sectionQuery))
            return null;

        var query = sectionQuery.Trim().ToLowerInvariant();

        if (query is "all" or "all sections" or "entire document" or "complete document" or "whole document")
            return null; // Signals caller to use full text

        // Match by section number e.g. "1" or "section 1" or "chapter 2"
        var matchNumber = Regex.Match(query, @"\b(\d+)\b");
        if (matchNumber.Success && int.TryParse(matchNumber.Value, out var num))
        {
            var match = sections.FirstOrDefault(s => s.Number == num);
            if (match is not null) return match.Content;
        }

        // Match by title substring
        var titleMatch = sections.FirstOrDefault(s => s.Title.ToLowerInvariant().Contains(query) ||
                                                      query.Contains(s.Title.ToLowerInvariant()));
        if (titleMatch is not null) return titleMatch.Content;

        return null;
    }
}
