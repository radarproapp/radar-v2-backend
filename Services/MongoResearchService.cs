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

    public async Task<List<ContentItem>> SearchPapersAsync(string query, int yearFrom = 0, int page = 1, int pageSize = 20)
    {
        return await _openAlex.SearchAsync(query, yearFrom, page, pageSize);
    }

    public async Task<ContentItem?> GetPaperByIdAsync(string id)
    {
        // Accept both OpenAlex IDs (W...) and DOIs
        if (id.StartsWith("W"))
            return await _openAlex.GetWorkByIdAsync(id);

        // If it looks like a DOI, search by DOI
        var results = await _openAlex.SearchAsync(id, pageSize: 1);
        return results.FirstOrDefault();
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
}
