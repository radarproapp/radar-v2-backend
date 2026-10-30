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
    private readonly ILogger<MongoResearchService> _logger;

    public MongoResearchService(
        OpenAlexClient openAlex,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        IAiEngine ai,
        ILogger<MongoResearchService> logger)
    {
        _openAlex = openAlex;
        _httpFactory = httpFactory;
        _config = config;
        _ai = ai;
        _logger = logger;
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

        // OpenAlex, and nothing else.
        //
        // A hand-written catalogue of eight papers used to be returned ahead of these. It
        // described itself as "high-impact, peer-reviewed ... with real DOIs, verified publisher
        // sources" and it was none of those things: six of its eight DOIs resolve to nothing at
        // doi.org, and the authors, journals, methodologies and statistics — "β = 0.472,
        // p < 0.001", "23.4% superior EBITDA recovery" — were written by hand. The two real DOIs
        // belonged to arXiv preprints whose findings had been paraphrased into the same template.
        //
        // The file sat eight lines below a comment promising "no synthetic-paper fallback ...
        // inventing a plausible-looking paper, DOI or statistic would violate the 'never invent
        // academic information' rule the product is built on". It also sat behind a citation
        // endpoint, which is what makes it worse than a bad feed row: a student could export
        // "Rostova, E., Vance, M., Adeleke, O., & Chen, D. K. (2023)" into a reference list for
        // a paper that does not exist.
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
        catch (ResearchUnavailableException e)
        {
            // Logged and rethrown. The message a reader sees is written where the failure is
            // known, which is the client - this used to construct its own, which only looked
            // right because the client never let anything through to it.
            _logger.LogWarning(e, "OpenAlex search failed for {Query}", query);
            throw;
        }

        // Deliberately no synthetic-paper fallback: an empty result set is the honest answer when
        // no verifiable work matches. Inventing a plausible-looking paper, DOI or statistic would
        // violate the "never invent academic information" rule the product is built on.

        // Ranked rather than handed over in the index's own order, which is what used to
        // happen, under a spec asking for credible and relevant research first.
        //
        // Within the page, to be exact. OpenAlex is asked for one page and returns it, so this
        // reorders those results and cannot reach past them - fine for the first page, which is
        // the one almost everybody reads, and worth knowing before trusting page two, where the
        // ordering across pages is not monotonic.
        var papers = ResearchRanking.Ordered(combined, profile).Take(pageSize * page).ToList();
        ExplainForReader(papers, profile);
        return papers;
    }

    /// <summary>
    /// One paper, by its OpenAlex id.
    /// </summary>
    /// <param name="withSummary">
    /// Whether to build the plain-English summary, which is a model call taking seconds.
    /// </param>
    /// <remarks>
    /// The summary used to be built unconditionally, and three callers that never read it went
    /// through here: the citation formatter, the explainer and the gap finder. So pressing "Cite"
    /// - the cheapest thing on the card - waited on a 500-word generation and then threw it away.
    /// </remarks>
    public async Task<ContentItem?> GetPaperByIdAsync(
        string id,
        UserProfile? profile = null,
        bool withSummary = true)
    {
        ContentItem? paper = null;

        if (id.StartsWith("W", StringComparison.OrdinalIgnoreCase))
        {
            paper = await _openAlex.GetWorkByIdAsync(id);
        }
        else
        {
            // Not an OpenAlex work id. Searching for it would answer with whatever ranks first
            // for those characters, so /api/research/foo/citation would format a citation for a
            // paper nobody asked for - on the one endpoint a reader pastes into a bibliography.
            return null;
        }

        if (paper is null) return null;

        if (withSummary) await EnsureAiPaperBreakdownAsync(paper);

        ExplainForReader([paper], profile);
        return paper;
    }

    public async Task<List<ContentItem>> GetRelatedPapersAsync(string paperId)
    {
        return await _openAlex.GetRelatedWorksAsync(paperId);
    }

    public async Task<string> ExplainSimplyAsync(string paperId)
    {
        var paper = await GetPaperByIdAsync(paperId, withSummary: false);
        if (paper is null)
            return "Paper not found. Please check the ID and try again.";

        var prompt = $"""
            Explain this research paper in simple, accessible language for a learner:

            Title: {paper.Title}
            Authors: {string.Join(", ", paper.Authors)}
            Journal: {paper.Journal}
            Abstract: {paper.WhatHappened}

            Explain what the paper is about, why it matters, and what the key findings are — without using jargon.
            """;

        return await CallLlmAsync("research-explain", prompt);
    }

    public async Task<string> FindResearchGapsAsync(string paperId)
    {
        var paper = await GetPaperByIdAsync(paperId, withSummary: false);
        if (paper is null)
            return "Paper not found. Please check the ID and try again.";

        var prompt = $"""
            Based on this research paper, identify key research gaps and future directions:

            Title: {paper.Title}
            Authors: {string.Join(", ", paper.Authors)}
            Journal: {paper.Journal}
            Abstract: {paper.WhatHappened}
            Key Findings: {paper.WhyItMatters}

            What questions remain unanswered? What would be logical next steps for researchers in this area?
            List 3-5 specific research gaps.
            """;

        return await CallLlmAsync("research-gaps", prompt);
    }

    public async Task<string> GetCitationAsync(string paperId, string style = "APA")
    {
        var paper = await GetPaperByIdAsync(paperId, withSummary: false);
        if (paper is null)
            return string.Empty;

        return OpenAlexClient.FormatCitation(paper, style);
    }

    /// <summary>
    /// Both research features ask different questions, so they are named differently.
    ///
    /// They shared the feature name "research-analysis", which meant the engine could not tell
    /// them apart when a call failed: somebody who asked what a paper leaves open was told Radar
    /// "could not write a plain-English summary of this one", which is an answer to the other
    /// button.
    /// </summary>
    private async Task<string> CallLlmAsync(string feature, string prompt)
    {
        return await _ai.GenerateTextAsync(feature, "You are Radar's research assistant. Be clear, concise, evidence-aware and helpful. Use markdown for structure.", prompt)
            ?? "Unable to generate analysis right now. Please try again later.";
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]", "");
    }

    /// <summary>
    /// The plain-English summary of a paper, built once and attached to it.
    /// </summary>
    /// <remarks>
    /// Written for somebody who is not in the field. The prompt used to ask for a "comprehensive
    /// introduction contextualizing the paper within the field" and got exactly that: a second
    /// abstract, in the same register as the first, which is no use to a student deciding whether
    /// a paper is worth two hours. Simple words and short sentences are the point of the feature,
    /// not a stylistic preference.
    ///
    /// Built from the title and the abstract, because that is all Radar has. So the instruction
    /// that matters most is the one about not filling gaps: a paper whose abstract does not say
    /// how the study was done gets a methodology section saying so. The alternative is a
    /// confident paragraph about a sample size nobody reported, in a product whose whole claim is
    /// that it does not invent academic information.
    /// </remarks>
    private async Task EnsureAiPaperBreakdownAsync(ContentItem paper)
    {
        if (paper.PaperBreakdown != null && paper.WordCount > 400) return;

        try
        {
            var prompt = $$"""
                Summarise this research paper for a reader who is not in its field.

                Title: {{paper.Title}}
                Authors: {{string.Join(", ", paper.Authors)}}
                Journal: {{paper.Journal ?? paper.Source ?? "not recorded"}}
                Year: {{paper.PublishedAt.Year}}
                Topic: {{paper.Topic ?? "not recorded"}}
                Abstract: {{paper.WhatHappened ?? paper.AiSummary ?? ""}}

                How to write it:
                - Very simple English. Short sentences. The words somebody would use out loud.
                - No jargon unless you explain it in the same sentence, in brackets.
                - Say what the paper says, not what you think of it.

                How long:
                - The introduction is 30 to 50 words. It is the orientation, not the summary:
                  what the paper is about and what field it sits in, and nothing else.
                - The five sections after it - problem statement, objective of the research,
                  methodology, analysis and findings, conclusion - carry the rest: about 90 words
                  each and no fewer than 70, so the six together come to about 500 words.
                - The remaining sections are supplementary. One or two sentences each, and only
                  where the abstract supports them.
                - Reach that by explaining, not by padding. Say what the technical words mean,
                  what the field is, and why the problem is hard for somebody meeting it for the
                  first time. A reader who does not know the field needs those sentences, and
                  they are the whole reason this exists instead of the abstract.
                - Where a section has no source, say so in one line and put the words into the
                  sections that do. Do not spread a thin abstract across nine headings.

                What you must not do:
                - Do not add anything that is not in the title or abstract above. No sample sizes,
                  statistics, methods, places or dates that are not stated there.
                - Where the abstract does not say, write exactly: {{AcademicPaperSynthesizer.NotStated}}
                  That is a useful answer. An invented one is not.

                Respond ONLY with valid JSON:
                {
                  "introduction": "30-50 words: what this paper is about and what field it sits in",
                  "problem": "the problem statement - what the researchers set out to solve, and why it mattered",
                  "aimAndObjectives": "the objective of the research - what they were trying to find out",
                  "methodology": "the methodology that was used, as far as the abstract says",
                  "findings": "the analysis and the findings, including any numbers the abstract gives",
                  "conclusion": "what the authors conclude from it",
                  "keyArguments": "supplementary, one or two sentences: the main points the authors make",
                  "contribution": "supplementary, one or two sentences: what this adds that was not there before",
                  "whyItMatters": "supplementary, one or two sentences: what somebody outside the field could do with this"
                }
                """;

            var breakdown = await _ai.GenerateJsonAsync<AcademicPaperBreakdown>(
                "academic-paper-breakdown",
                "You summarise research papers in plain English for readers outside the field. You "
                + "never state anything the paper's own abstract does not, and you say so plainly "
                + "where it is silent.",
                prompt);

            if (breakdown != null && !string.IsNullOrWhiteSpace(breakdown.Introduction))
            {
                paper.PaperBreakdown = breakdown;
                AcademicPaperSynthesizer.EnsureDerivedParts(paper);
                paper.WordCount = breakdown.TotalWordCount;
                return;
            }
        }
        catch
        {
            // Fall back to the template synthesizer, which works from the same abstract.
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
        // Era does not depend on the reader, so it is set before the profile check - a signed-out
        // search still needs to know a 1998 paper is foundational.
        //
        // Set on the item rather than through EnsureEra, which writes into the breakdown and
        // returns immediately when there is not one. Search results never have a breakdown -
        // it is a model call made when a paper is opened - so that loop did nothing at all on
        // every surface the era was for, and the comment above it was false.
        foreach (var paper in papers) paper.PaperEra = ResearchRanking.EraOf(paper);

        if (profile is null) return;

        foreach (var paper in papers)
        {

            var explanation = InterestPersonalization.Explain(profile, paper);
            paper.MatchedSignals = explanation.MatchedSignals.ToList();
            paper.WhyYoureSeeingThis = WhyShown.Build(paper, explanation.MatchedSignals);
        }
    }

}
