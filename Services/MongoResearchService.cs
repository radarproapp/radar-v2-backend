using System.Text;
using System.Text.Json;
using RadarV2.Models;
using RadarV2.Services.Ingestion;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Real implementation of IResearchService using OpenAlex for paper search/retrieval
/// and OpenRouter LLM for AI-powered features (ExplainSimply, FindResearchGaps).
/// </summary>
public class MongoResearchService : IResearchService
{
    private readonly OpenAlexClient _openAlex;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IAiEngine _ai;

    public MongoResearchService(
        OpenAlexClient openAlex,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IAiEngine ai)
    {
        _openAlex = openAlex;
        _httpFactory = httpFactory;
        _config = config;
        _ai = ai;
    }

    /// <summary>
    /// Finds papers, and says why each one is in front of this reader.
    ///
    /// <paramref name="profile"/> is optional because several callers have no user in scope. When
    /// it is absent the paper carries no "why", which is better than a generic one: "relevant to
    /// your research interests" is the sentence a reader learns to skip.
    /// </summary>
    public async Task<List<ContentItem>> SearchPapersAsync(string query, int yearFrom = 0, string? paperType = null, int page = 1, int pageSize = 25, UserProfile? profile = null)
    {
        var combined = new List<ContentItem>();
        var seenDois = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. First retrieve curated, verified papers matching the topic
        var catalogMatches = ResearchPaperCatalog.Search(query, yearFrom, paperType);
        foreach (var paper in catalogMatches)
        {
            if (!string.IsNullOrEmpty(paper.Doi)) seenDois.Add(paper.Doi);
            seenTitles.Add(NormalizeTitle(paper.Title));
            combined.Add(paper);
        }

        // 2. Query OpenAlex across 250M+ scientific records
        try
        {
            var liveResults = await _openAlex.SearchAsync(query, yearFrom, page, pageSize);
            foreach (var paper in liveResults)
            {
                // Filter by paperType if requested
                if (!string.IsNullOrWhiteSpace(paperType) && paperType != "all")
                {
                    var pt = paperType.Trim().ToLowerInvariant();
                    if (!(paper.DocumentType ?? "").ToLowerInvariant().Contains(pt))
                        continue;
                }

                if (!string.IsNullOrEmpty(paper.Doi) && seenDois.Contains(paper.Doi))
                    continue;

                var normTitle = NormalizeTitle(paper.Title);
                if (seenTitles.Contains(normTitle))
                    continue;

                if (!string.IsNullOrEmpty(paper.Doi)) seenDois.Add(paper.Doi);
                seenTitles.Add(normTitle);
                combined.Add(paper);
            }
        }
        catch
        {
            // If external OpenAlex call fails or times out, curated catalog still powers the response
        }

        // Deliberately no synthetic-paper fallback: an empty result set is the honest answer when
        // no verifiable work matches. Inventing a plausible-looking paper, DOI or statistic would
        // violate the "never invent academic information" rule the product is built on.

        var papers = combined.Take(pageSize * page).ToList();
        ExplainForReader(papers, profile);
        return papers;
    }

    public async Task<ContentItem?> GetPaperByIdAsync(string id, UserProfile? profile = null)
    {
        // Check curated catalog first
        var local = ResearchPaperCatalog.GetAll().FirstOrDefault(p =>
            p.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(p.Doi) && p.Doi.Equals(id, StringComparison.OrdinalIgnoreCase)));

        if (local != null)
            return local;

        ContentItem? paper = null;
        if (id.StartsWith("W", StringComparison.OrdinalIgnoreCase))
            paper = await _openAlex.GetWorkByIdAsync(id);
        else
        {
            var results = await _openAlex.SearchAsync(id, pageSize: 1);
            paper = results.FirstOrDefault();
        }

        if (paper != null)
        {
            await EnsureAiPaperBreakdownAsync(paper);
        }

        if (paper is not null) ExplainForReader([paper], profile);
        return paper;
    }

    public async Task<List<ContentItem>> GetRelatedPapersAsync(string paperId)
    {
        return await _openAlex.GetRelatedWorksAsync(paperId);
    }

    public async Task<string> ExplainSimplyAsync(string paperId)
    {
        var paper = await GetPaperByIdAsync(paperId);
        if (paper is null)
            return "Paper not found. Please check the ID and try again.";

        var prompt = $"""
            Explain this research paper in simple, accessible language for a learner:

            Title: {paper.Title}
            Authors: {string.Join(", ", paper.Authors)}
            Journal: {paper.Journal}
            Abstract: {paper.AiSummary}

            Explain what the paper is about, why it matters, and what the key findings are — without using jargon.
            """;

        return await CallLlmAsync(prompt);
    }

    public async Task<string> FindResearchGapsAsync(string paperId)
    {
        var paper = await GetPaperByIdAsync(paperId);
        if (paper is null)
            return "Paper not found. Please check the ID and try again.";

        var prompt = $"""
            Based on this research paper, identify key research gaps and future directions:

            Title: {paper.Title}
            Authors: {string.Join(", ", paper.Authors)}
            Journal: {paper.Journal}
            Abstract: {paper.AiSummary}
            Key Findings: {paper.WhyItMatters}

            What questions remain unanswered? What would be logical next steps for researchers in this area?
            List 3-5 specific research gaps.
            """;

        return await CallLlmAsync(prompt);
    }

    public async Task<string> GetCitationAsync(string paperId, string style = "APA")
    {
        var paper = await GetPaperByIdAsync(paperId);
        if (paper is null)
            return string.Empty;

        return OpenAlexClient.FormatCitation(paper, style);
    }

    private async Task<string> CallLlmAsync(string prompt)
    {
        return await _ai.GenerateTextAsync("research-analysis", "You are Radar's research assistant. Be clear, concise, evidence-aware and helpful. Use markdown for structure.", prompt)
            ?? "Unable to generate analysis right now. Please try again later.";
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", "");
    }

    private async Task EnsureAiPaperBreakdownAsync(ContentItem paper)
    {
        if (paper.PaperBreakdown != null && paper.WordCount > 400) return;

        try
        {
            var prompt = $$"""
                Synthesize a strict ~500-word academic breakdown for the following research paper:
                Title: {{paper.Title}}
                Authors: {{string.Join(", ", paper.Authors)}}
                Journal: {{paper.Journal ?? paper.Source ?? "Peer-Reviewed Press"}}
                Year: {{paper.PublishedAt.Year}}
                Abstract: {{paper.AiSummary ?? paper.WhatHappened ?? ""}}
                Topic: {{paper.Topic ?? "Interdisciplinary Science"}}

                Format strictly as JSON with all 7 mandated sections:
                {
                  "introduction": "Comprehensive introduction contextualizing the paper within the field...",
                  "problem": "Specific, empirical problem statement the researchers target...",
                  "aimAndObjectives": "Key aim and structured objectives of the investigation...",
                  "methodology": "Empirical methodology, datasets, models, or experimental setup used...",
                  "keyArguments": "Primary theoretical arguments and claims articulated by the authors...",
                  "findings": "Verified quantitative and qualitative findings produced by the study...",
                  "contribution": "Direct scientific contribution to the field and why it matters to practitioners...",
                  "whyItMatters": "Actionable explanation connecting this academic evidence to real-world application."
                }
                """;

            var breakdown = await _ai.GenerateJsonAsync<AcademicPaperBreakdown>(
                "academic-paper-breakdown",
                "You are Radar's Academic Research Synthesizer. Produce strict, verified 7-part 500-word academic paper breakdowns.",
                prompt);

            if (breakdown != null && !string.IsNullOrWhiteSpace(breakdown.Introduction))
            {
                paper.PaperBreakdown = breakdown;
                paper.WordCount = breakdown.TotalWordCount;
                return;
            }
        }
        catch
        {
            // Fall back to template synthesizer cleanly
        }

        AcademicPaperSynthesizer.EnrichPaperWithStrictBreakdown(paper);
    }
    /// <summary>
    /// Says why each paper is in front of this particular reader.
    ///
    /// Spec 8.5. Reuses the scorer the feed uses rather than writing a second one, which matters
    /// for a reason beyond saving code: that scorer builds its sentence out of the dimensions it
    /// actually matched on, so it cannot claim a connection nothing computed. A hand-written
    /// "relevant to your research interests" would be true of everything and therefore worth
    /// nothing.
    ///
    /// With no profile the paper carries no "why" at all. An unsigned-in reader is better served
    /// by silence than by a sentence about interests Radar does not know.
    /// </summary>
    private static void ExplainForReader(IReadOnlyList<ContentItem> papers, UserProfile? profile)
    {
        if (profile is null) return;

        foreach (var paper in papers)
        {
            var explanation = InterestPersonalization.Explain(profile, paper);
            paper.MatchedSignals = explanation.MatchedSignals.ToList();
            paper.WhyYoureSeeingThis = WhyShown.Build(paper, explanation.MatchedSignals);
        }
    }

}
