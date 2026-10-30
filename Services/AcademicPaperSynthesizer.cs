using System.Text;
using System.Text.RegularExpressions;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Synthesizes and enforces strict 5-minute (~500-word) academic breakdowns for research papers,
/// adhering strictly to the mandated 7-part breakdown format:
/// 1. Introduction
/// 2. Problem Statement
/// 3. Key Aim & Objectives
/// 4. Methodology
/// 5. Key Arguments
/// 6. Findings
/// 7. Contribution to Field & Why It Matters
/// Also handles standard multi-format citation generation (APA 7th, Harvard, IEEE, BibTeX, MLA).
/// </summary>
public static class AcademicPaperSynthesizer
{
    /// <summary>
    /// Ensures the paper has a complete, strictly formatted ~500-word academic breakdown.
    /// If one is not pre-populated, this synthesizes a comprehensive 7-part breakdown
    /// from the paper's title, abstract, authors, journal, and citation signals.
    /// </summary>
    /// <summary>
    /// Fills in the derived parts of a breakdown that arrived already built.
    ///
    /// Search results skip the full enricher - they come straight from the catalogue or from
    /// OpenAlex with a breakdown attached - so any section added after those breakdowns were
    /// written reaches the reader empty. That happened once with <c>Era</c> and again with
    /// Limitations and FutureWork, which is why this is now a general backfill rather than a
    /// fix for one field: the next part added will be covered without anyone remembering.
    ///
    /// Only fills what is empty, and only from what the paper already carries. Era comes from
    /// the publication date; the other two are extracted from the abstract on the same terms as
    /// every other section, and say <see cref="NotStated"/> when the abstract is silent.
    /// </summary>
    public static void EnsureDerivedParts(ContentItem paper)
    {
        if (paper.PaperBreakdown is null) return;

        if (string.IsNullOrWhiteSpace(paper.PaperBreakdown.Era))
        {
            paper.PaperBreakdown.Era = EraFor(paper);
        }

        var abstractText = CleanText(
            !string.IsNullOrWhiteSpace(paper.WhatHappened) ? paper.WhatHappened : paper.AiSummary ?? string.Empty);

        if (string.IsNullOrWhiteSpace(paper.PaperBreakdown.Limitations))
        {
            paper.PaperBreakdown.Limitations = FromSource(
                abstractText, "limitation", "however", "caveat", "constrain", "we did not", "does not", "cannot", "small sample", "future");
        }

        if (string.IsNullOrWhiteSpace(paper.PaperBreakdown.FutureWork))
        {
            paper.PaperBreakdown.FutureWork = FromSource(
                abstractText, "future work", "further research", "next step", "remains to be", "we plan", "should be investigated", "open question");
        }

        if (string.IsNullOrWhiteSpace(paper.PaperBreakdown.Conclusion))
        {
            paper.PaperBreakdown.Conclusion = FromSource(abstractText, "we conclude", "in conclusion", "concludes", "overall", "taken together", "these results suggest", "we argue that", "this shows");
        }

        if (string.IsNullOrWhiteSpace(paper.PaperBreakdown.WhyItMatters))
        {
            // Facts only, exactly as the full enricher states them. A hand-written catalogue
            // entry that skipped this line left the reader with no provenance at all.
            var journal = !string.IsNullOrWhiteSpace(paper.Journal)
                ? paper.Journal
                : (!string.IsNullOrWhiteSpace(paper.Source) ? paper.Source : "Source not recorded");
            var year = paper.PublishedAt.Year > 1900 ? paper.PublishedAt.Year : DateTime.UtcNow.Year;

            paper.PaperBreakdown.WhyItMatters = string.IsNullOrWhiteSpace(paper.Doi)
                ? $"Published in {journal} ({year}). No DOI on record."
                : $"Published in {journal} ({year}). DOI: {paper.Doi}";
        }
    }

    /// <summary>Kept for callers that only wanted the era. Delegates, so there is one backfill.</summary>
    public static void EnsureEra(ContentItem paper) => EnsureDerivedParts(paper);

    public static void EnrichPaperWithStrictBreakdown(ContentItem paper)
    {
        // Era is derived from the publication date, not from the text, so it costs nothing and is
        // set even when a breakdown already exists. Putting it after the early return meant every
        // paper that arrived with a pre-built breakdown - which is most of them - had an empty
        // Era, and the whole point of 8.4 is that a reader sees it without having to check a year.
        if (paper.PaperBreakdown is not null && string.IsNullOrWhiteSpace(paper.PaperBreakdown.Era))
        {
            paper.PaperBreakdown.Era = EraFor(paper);
        }

        if (paper.PaperBreakdown != null && paper.WordCount > 400)
            return;

        var title = !string.IsNullOrWhiteSpace(paper.Title) ? paper.Title : "Untitled paper";
        // "Peer-Reviewed Academic Press" was invented, and asserted peer review that nothing checked.
        var journal = !string.IsNullOrWhiteSpace(paper.Journal) ? paper.Journal : (!string.IsNullOrWhiteSpace(paper.Source) ? paper.Source : "Source not recorded");
        var authorsList = paper.Authors.Count > 0 ? string.Join(", ", paper.Authors.Take(3)) + (paper.Authors.Count > 3 ? " et al." : "") : "The authors";
        var year = paper.PublishedAt.Year > 1900 ? paper.PublishedAt.Year : DateTime.UtcNow.Year;
        var abstractText = !string.IsNullOrWhiteSpace(paper.WhatHappened) ? paper.WhatHappened : (!string.IsNullOrWhiteSpace(paper.AiSummary) ? paper.AiSummary : string.Empty);
        var topic = paper.Tags.Count > 0 ? paper.Tags[0] : (!string.IsNullOrWhiteSpace(paper.Topic) ? paper.Topic : string.Empty);

        // Clean abstract
        abstractText = CleanText(abstractText);

        // Synthesize the 7 strict sections
        var intro = GenerateIntroduction(title, topic, journal, year, abstractText);
        var problem = GenerateProblem(title, topic, abstractText);
        var aim = GenerateAimAndObjectives(title, topic, abstractText);
        var method = GenerateMethodology(title, topic, abstractText, paper.Methodology);
        var args = GenerateKeyArguments(title, topic, abstractText);
        var findings = GenerateFindings(title, abstractText, paper.KeyFindings, paper.WhyItMatters);
        var contribution = GenerateContribution(title, topic, journal, abstractText);

        paper.PaperBreakdown = new AcademicPaperBreakdown
        {
            Introduction = intro,
            Problem = problem,
            AimAndObjectives = aim,
            Methodology = method,
            KeyArguments = args,
            Findings = findings,
            Contribution = contribution,
            // Facts only. This line used to read "Sourced from verifiable databases with verified
            // DOI" - nothing here verifies anything, and when there was no DOI it invented
            // "Direct Open Access" to fill the slot. A claim of verification is exactly the claim
            // a reader cannot check for themselves.
            WhyItMatters = string.IsNullOrWhiteSpace(paper.Doi)
                ? $"Published in {journal} ({year}). No DOI on record."
                : $"Published in {journal} ({year}). DOI: {paper.Doi}",

            // Spec 8.4: where the work sits in time, so a 1998 foundational paper is not read as
            // current practice. Derived from the publication date rather than asserted.
            Era = EraFor(paper),

            // Spec 8.2. Both extracted from the abstract on the same terms as every other
            // section: present when the authors said it, NotStated when they did not.
            Limitations = FromSource(abstractText, "limitation", "however", "caveat", "constrain", "we did not", "does not", "cannot", "small sample", "future"),
            FutureWork = FromSource(abstractText, "future work", "further research", "next step", "remains to be", "we plan", "should be investigated", "open question"),

            // On the same terms as every other extracted section: the authors' own concluding
            // sentences where they wrote them, and NotStated where they did not. A conclusion
            // assembled from the title would be Radar's conclusion, attributed to them.
            Conclusion = FromSource(abstractText, "we conclude", "in conclusion", "concludes", "overall", "taken together", "these results suggest", "we argue that", "this shows")
        };

        // Calculate total word count
        var totalWords = CountWords(intro) + CountWords(problem) + CountWords(aim) + 
                         CountWords(method) + CountWords(args) + CountWords(findings) + 
                         CountWords(contribution);

        paper.WordCount = totalWords;
        paper.EstimatedReadTime = $"{Math.Max(5, (int)Math.Ceiling(totalWords / 110.0))} min read";

        if (string.IsNullOrWhiteSpace(paper.KeyFindings))
            paper.KeyFindings = ExtractKeyTakeaway(findings);

        if (string.IsNullOrWhiteSpace(paper.Methodology))
            paper.Methodology = ExtractKeyTakeaway(method);
    }

    /// <summary>
    /// Generates multi-style academic citations for research verification.
    /// </summary>
    public static string FormatCitation(ContentItem paper, string style = "APA")
    {
        var authors = paper.Authors.Count > 0
            ? FormatAuthorsForStyle(paper.Authors, style)
            : "Unknown Author";

        var year = paper.PublishedAt.Year > 1900 ? paper.PublishedAt.Year : DateTime.UtcNow.Year;
        var title = paper.Title.TrimEnd('.');
        var journal = !string.IsNullOrWhiteSpace(paper.Journal) ? paper.Journal : paper.Source;
        var doi = paper.Doi?.Replace("https://doi.org/", "").Trim();
        var url = !string.IsNullOrWhiteSpace(paper.Url) ? paper.Url : (!string.IsNullOrEmpty(doi) ? $"https://doi.org/{doi}" : "");

        // The venue and the punctuation that follows it, or neither.
        //
        // OpenAlex returns no venue for plenty of records - preprints, dissertations, datasets -
        // and a reference list should then simply not name one. The gap used to be filled with
        // "Academic Press", a real Elsevier imprint, so a student pasted a false publisher into
        // their bibliography. Leaving the segment out is the honest version, and leaving an empty
        // one in would put two full stops where the publisher should be.
        string Venue(string separator) =>
            string.IsNullOrWhiteSpace(journal) ? string.Empty : journal + separator;

        return style.ToUpperInvariant() switch
        {
            "APA" or "APA7" =>
                $"{authors} ({year}). {title}. {Venue(". ")}{(string.IsNullOrEmpty(doi) ? url : $"https://doi.org/{doi}")}",

            "HARVARD" =>
                $"{authors}, {year}. {title}. {Venue(". ")}Available at: <{(string.IsNullOrEmpty(doi) ? url : $"https://doi.org/{doi}")}>.",

            "IEEE" =>
                $"[1] {FormatAuthorsIeee(paper.Authors)}, \"{title},\" {Venue(", ")}{year}{(string.IsNullOrEmpty(doi) ? "" : $", doi: {doi}")}.",

            "BIBTEX" =>
                BuildBibtexCitation(paper, authors, year, title, journal, doi, url),

            "MLA" =>
                $"{authors}. \"{title}.\" {Venue(", ")}{year}, {(string.IsNullOrEmpty(doi) ? url : $"doi:{doi}")}.",

            "CHICAGO" =>
                $"{authors}. \"{title}.\" {Venue(" ")}({year}). {(string.IsNullOrEmpty(doi) ? url : $"https://doi.org/{doi}")}.",

            _ => $"{authors} ({year}). {title}. {Venue(". ")}https://doi.org/{doi}"
        };
    }

    private static string BuildBibtexCitation(ContentItem paper, string authors, int year, string title, string journal, string? doi, string url)
    {
        var cleanId = Regex.Replace(paper.Id, @"[^a-zA-Z0-9_]", "_");
        if (cleanId.Length > 20) cleanId = cleanId[..20];
        var firstAuthor = paper.Authors.Count > 0 ? Regex.Replace(paper.Authors[0], @"\s+", "").ToLowerInvariant() : "ref";
        var key = $"{firstAuthor}{year}_{cleanId}";

        var sb = new StringBuilder();
        sb.AppendLine($"@article{{{key},");
        sb.AppendLine($"  title = {{{title}}},");
        sb.AppendLine($"  author = {{{string.Join(" and ", paper.Authors)}}},");
        if (!string.IsNullOrWhiteSpace(journal))
            sb.AppendLine($"  journal = {{{journal}}},");
        sb.AppendLine($"  year = {{{year}}},");
        if (!string.IsNullOrEmpty(doi))
            sb.AppendLine($"  doi = {{{doi}}},");
        if (!string.IsNullOrEmpty(url))
            sb.AppendLine($"  url = {{{url}}},");
        // Where the record came from, and nothing about its standing. This asserted a
        // "Verifiable Peer-Reviewed Record" on every export, including preprints, and a BibTeX
        // note goes wherever the bibliography goes. Radar does not check peer review and has no
        // way to.
        sb.AppendLine($"  note = {{Record from OpenAlex via Radar}}");
        sb.Append('}');
        return sb.ToString();
    }

    private static string FormatAuthorsForStyle(List<string> authors, string style)
    {
        if (authors.Count == 0) return "Unknown";
        if (authors.Count == 1) return authors[0];
        if (authors.Count == 2) return $"{authors[0]} & {authors[1]}";
        return $"{authors[0]} et al.";
    }

    private static string FormatAuthorsIeee(List<string> authors)
    {
        if (authors.Count == 0) return "Unknown";
        if (authors.Count == 1) return authors[0];
        if (authors.Count == 2) return $"{authors[0]} and {authors[1]}";
        return $"{authors[0]} et al.";
    }

    /// <summary>
    /// What a section says when the source does not support it.
    ///
    /// Spec #18 §6. Every generator below used to invent prose instead: a paper with no stated
    /// methodology was described as using "an empirical, multi-tiered methodological framework
    /// combining quantitative modeling, systematic observational sampling, and iterative
    /// sensitivity benchmarking", and every paper was credited with "strict counterfactual
    /// controls, statistical error bounds, and reproducible validation metrics" whether it had
    /// any or not. The aim section did not read the abstract at all - it asserted the same three
    /// objectives for every paper in the database.
    ///
    /// A reader cannot tell invented scholarship from real scholarship when both are written in
    /// the same register. Saying nothing is the only safe answer.
    /// </summary>
    public const string NotStated = "Not clearly stated in the available source.";

    /// <summary>
    /// The sentences of the abstract that actually speak to a section, or <see cref="NotStated"/>.
    ///
    /// Extraction, not generation: every word returned here came from the source. Where the
    /// abstract says nothing on the subject, the section says so, and the summary is shorter than
    /// it would have been. A shorter honest summary is the point.
    /// </summary>
    private static string FromSource(string abstractText, params string[] cues)
    {
        if (string.IsNullOrWhiteSpace(abstractText)) return NotStated;

        var pattern = @"\b(" + string.Join("|", cues) + @")";
        var matching = SplitSentences(abstractText)
            .Where(sentence => Regex.IsMatch(sentence, pattern, RegexOptions.IgnoreCase))
            .Take(3)
            .ToList();

        return matching.Count > 0 ? string.Join(" ", matching).Trim() : NotStated;
    }

    private static string GenerateIntroduction(string title, string topic, string journal, int year, string abstractText)
    {
        // The one section that can be written from metadata alone, because the metadata is fact:
        // this paper, this journal, this year. Everything beyond that comes from the abstract.
        var opening = $"\"{title}\", published in {journal} ({year}).";

        if (string.IsNullOrWhiteSpace(abstractText)) return opening + " " + NotStated;

        var firstSentences = GetSentenceRange(abstractText, 0, 2);
        return string.IsNullOrWhiteSpace(firstSentences)
            ? opening + " " + NotStated
            : $"{opening} The authors open: \"{firstSentences.Trim()}\"";
    }

    private static string GenerateProblem(string title, string topic, string abstractText) =>
        FromSource(abstractText, "problem", "challeng", "gap", "limitation", "lack", "however", "despite", "fail", "difficult");

    private static string GenerateAimAndObjectives(string title, string topic, string abstractText) =>
        FromSource(abstractText, "aim", "objective", "purpose", "goal", "we propose", "this paper", "this study", "we present", "we introduce", "investigat");

    private static string GenerateMethodology(string title, string topic, string abstractText, string? existingMethod)
    {
        if (!string.IsNullOrWhiteSpace(existingMethod)) return existingMethod.Trim();

        return FromSource(abstractText, "method", "data", "sampl", "model", "evaluat", "experi", "survey", "measur", "analy", "framework", "dataset", "simulat");
    }

    private static string GenerateKeyArguments(string title, string topic, string abstractText) =>
        FromSource(abstractText, "argue", "claim", "suggest", "propose", "we show", "demonstrat", "contend", "posit", "hypothes");

    private static string GenerateFindings(string title, string abstractText, string? existingFindings, string? whyMatters)
    {
        if (!string.IsNullOrWhiteSpace(existingFindings)) return existingFindings.Trim();

        return FromSource(abstractText, "find", "result", "show", "observ", "report", "improv", "outperform", "achiev", "reduc", "increas", "accuracy");
    }

    private static string GenerateContribution(string title, string topic, string journal, string abstractText) =>
        FromSource(abstractText, "contribut", "novel", "first", "new", "advance", "implication", "enable", "matter", "impact");

    /// <summary>
    /// Where a paper sits in time.
    ///
    /// Spec 8.4. A 1998 paper can be the foundational text of a field and still be the wrong
    /// thing to follow as current practice. Saying which it is costs one line and stops a reader
    /// treating a classic as a recommendation.
    /// </summary>
    private static string EraFor(ContentItem paper)
    {
        if (paper.PublishedAt.Year <= 0) return "Publication date not recorded";

        // One definition, shared with the ranking that uses it. This one read the year alone,
        // so anything older than fifteen years was "Foundational" whether or not a soul had
        // cited it - which is most of what gets published. Being old is not the same as being
        // the thing a field was built on, and saying so about an uncited paper is the kind of
        // claim this product exists not to make.
        return ResearchRanking.EraOf(paper) switch
        {
            ResearchRanking.Recent => "Recent",
            ResearchRanking.Foundational => "Foundational - predates current practice by design",
            _ => "Established",
        };
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = Regex.Replace(text, @"\s+", " ").Trim();
        return cleaned;
    }

    private static List<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return Regex.Split(text, @"(?<=[.!?])\s+").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static string GetSentenceRange(string text, int startIdx, int count)
    {
        var sentences = SplitSentences(text);
        if (sentences.Count == 0) return string.Empty;
        var slice = sentences.Skip(startIdx).Take(count);
        return string.Join(" ", slice);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string ExtractKeyTakeaway(string text)
    {
        var sentences = SplitSentences(text);
        return sentences.FirstOrDefault() ?? text;
    }
}
