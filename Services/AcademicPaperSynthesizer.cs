using System.Globalization;
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
            var year = paper.PublishedAt.Year > 1900 ? paper.PublishedAt.Year.ToString() : "date not recorded";

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
        var year = paper.PublishedAt.Year > 1900 ? paper.PublishedAt.Year.ToString() : "date not recorded";
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

        // The same six sections the model path counts, from the one definition of them.
        //
        // This added up its own list - KeyArguments and Contribution, which are supplementary,
        // and no Conclusion, which is one of the six - so the template and the model disagreed
        // about the length of the same summary by about a section.
        var totalWords = paper.PaperBreakdown.TotalWordCount;

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

        // "n.d." - no date - which is what APA, Harvard and MLA all use for a work whose date
        // is unknown. This printed the current year instead, so a paper of unrecorded age was
        // cited as published this year, in a reference list a student hands in.
        var year = paper.PublishedAt.Year > 1900 ? paper.PublishedAt.Year.ToString() : "n.d.";
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

        // Harvard's locator: a DOI where there is one, and otherwise the link with the date it
        // was read, which is what "Accessed" is for. Neither is invented - a record with no DOI
        // and no URL simply ends after the source.
        string Access()
        {
            if (!string.IsNullOrEmpty(doi)) return $"doi: {doi}";
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            // en-GB explicitly. The default formats with the host's culture, so a server in
            // Frankfurt would write "30 Oktober 2026" inside an English Harvard reference.
            return $"Available at: {url} (Accessed: {DateTime.UtcNow.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"))})";
        }

        // A full stop that does not double one already there.
        //
        // An author string now ends in an initial's period - "Vaswani, A." - and the MLA and
        // Chicago templates hard-coded their own, so every entry in those two styles came out
        // as "Vaswani, A.. \"Title.\"". The author list only started ending that way when it
        // became style-aware, so this is new, and the honesty test that guards against gaps
        // only looked for ". " with a space.
        string Stop(string text) => text.EndsWith('.') ? text : text + ".";

        return style.ToUpperInvariant() switch
        {
            // APA 7th. Author list inverted, year in brackets, sentence-case title, the source,
            // then the DOI as a link - which is the seventh edition's form. The sixth wrote
            // "doi:10.xxxx" and put "Retrieved from" before a URL; neither survives into 7.
            "APA" or "APA7" =>
                $"{authors} ({year}). {title}. {Venue(". ")}{(string.IsNullOrEmpty(doi) ? url : $"https://doi.org/{doi}")}".TrimEnd(),

            // Harvard, as Cite Them Right sets it out: year in brackets after the authors, the
            // article title in single quotes, then the source. This read "Vaswani, 2017. Title."
            // - the year loose after a comma and the title bare - which is neither Harvard nor
            // APA but a half-remembered blend of the two.
            "HARVARD" =>
                Stop($"{authors} ({year}) '{title}'{(string.IsNullOrWhiteSpace(journal) ? ". " : $", {journal}. ")}{Access()}".TrimEnd()),

            "IEEE" =>
                $"[1] {FormatAuthorsIeee(paper.Authors)}, \"{title},\" {Venue(", ")}{year}{(string.IsNullOrEmpty(doi) ? "" : $", doi: {doi}")}.",

            "BIBTEX" =>
                BuildBibtexCitation(paper, authors, year, title, journal, doi, url),

            "MLA" =>
                $"{Stop(authors)} \"{title}.\" {Venue(", ")}{year}, {(string.IsNullOrEmpty(doi) ? url : $"doi:{doi}")}.",

            "CHICAGO" =>
                $"{Stop(authors)} \"{title}.\" {Venue(" ")}({year}). {(string.IsNullOrEmpty(doi) ? url : $"https://doi.org/{doi}")}.",

            // Anything else is treated as APA rather than as its own half-written style. This
            // arm appended "https://doi.org/" unconditionally, so a paper with no DOI reached
            // an unrecognised style and produced a citation ending in a link to nothing - and
            // the style parameter comes straight off the query string, unvalidated.
            _ => $"{authors} ({year}). {title}. {Venue(". ")}{(string.IsNullOrEmpty(doi) ? url : $"https://doi.org/{doi}")}".TrimEnd()
        };
    }

    private static string BuildBibtexCitation(ContentItem paper, string authors, string year, string title, string journal, string? doi, string url)
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

    /// <summary>
    /// The author list for a reference entry, in the style asked for.
    /// </summary>
    /// <remarks>
    /// Three things were wrong with the version this replaces, and all three show up in a
    /// marked bibliography. It used the display name as the source gave it - "Ashish Vaswani"
    /// where a reference list wants "Vaswani, A." It collapsed to "et al." at three authors,
    /// which is the rule for an in-text citation and not for a reference entry: APA 7 lists up
    /// to twenty, Harvard lists up to three and then uses et al. And it used the APA ampersand
    /// for every style, including Harvard, which uses "and".
    /// </remarks>
    private static string FormatAuthorsForStyle(List<string> authors, string style)
    {
        if (authors.Count == 0) return "Unknown";

        var named = authors.Select(AuthorName.Inverted).Where(a => a.Length > 0).ToList();
        if (named.Count == 0) return "Unknown";

        return style.ToUpperInvariant() switch
        {
            "HARVARD" => Harvard(named),
            "MLA" or "CHICAGO" => FirstInverted(authors, style),
            _ => Apa(named),
        };
    }

    /// <summary>
    /// MLA and Chicago: only the first author is inverted, and given names are written in full.
    /// </summary>
    /// <remarks>
    /// These fell through to the APA arm, so they came out as "Vaswani, A., &amp; Shazeer, N." -
    /// APA initials and the APA ampersand, in an MLA entry. Before the author list became
    /// style-aware they used the raw display name, which was wrong in a different way; this is
    /// the first time either is right.
    ///
    /// MLA 9 and Chicago 17 agree on the shape: invert the first, leave the rest as written,
    /// join with "and". They part company on where "et al." begins, which is the parameter.
    /// </remarks>
    private static string FirstInverted(List<string> authors, string style)
    {
        var etAlFrom = style.Equals("MLA", StringComparison.OrdinalIgnoreCase) ? 3 : 11;

        var first = AuthorName.InvertedInFull(authors[0]);
        if (authors.Count == 1) return first;
        if (authors.Count >= etAlFrom) return $"{first}, et al.";

        var rest = authors.Skip(1).Select(AuthorName.AsWritten).ToList();
        if (rest.Count == 1) return $"{first}, and {rest[0]}";

        return $"{first}, {string.Join(", ", rest[..^1])}, and {rest[^1]}";
    }

    /// <summary>
    /// APA 7th: every author up to twenty, an ampersand before the last.
    /// </summary>
    /// <remarks>
    /// Past twenty, APA 7 lists the first nineteen, then an ellipsis, then the final author -
    /// and never "et al." in the reference list. The sixth edition stopped at seven, which is
    /// the rule most sources still repeat.
    /// </remarks>
    private static string Apa(List<string> named)
    {
        if (named.Count == 1) return named[0];
        if (named.Count == 2) return $"{named[0]}, & {named[1]}";

        if (named.Count <= 20)
            return $"{string.Join(", ", named[..^1])}, & {named[^1]}";

        return $"{string.Join(", ", named[..19])}, ... {named[^1]}";
    }

    /// <summary>
    /// Harvard: up to three authors joined with "and", et al. beyond that.
    /// </summary>
    private static string Harvard(List<string> named)
    {
        if (named.Count == 1) return named[0];
        if (named.Count == 2) return $"{named[0]} and {named[1]}";
        if (named.Count == 3) return $"{named[0]}, {named[1]} and {named[2]}";

        return $"{named[0]} et al.";
    }

    /// <summary>
    /// IEEE: initials first, then the surname - "A. Vaswani", not "Ashish Vaswani".
    /// </summary>
    /// <remarks>
    /// This handed back the display name untouched, so an IEEE reference read "Ashish Vaswani
    /// and Noam Shazeer" where the style wants "A. Vaswani and N. Shazeer". It was the one style
    /// the author work did not reach, because it has its own formatter.
    /// </remarks>
    private static string FormatAuthorsIeee(List<string> authors)
    {
        var named = authors.Select(AuthorName.InitialsFirst).Where(a => a.Length > 0).ToList();

        if (named.Count == 0) return "Unknown";
        if (named.Count == 1) return named[0];
        if (named.Count == 2) return $"{named[0]} and {named[1]}";
        if (named.Count > 6) return $"{named[0]} et al.";

        return $"{string.Join(", ", named[..^1])}, and {named[^1]}";
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

    private static string GenerateIntroduction(string title, string topic, string journal, string year, string abstractText)
    {
        // The one section that can be written from metadata alone, because the metadata is fact:
        // this paper, this journal, this year. Everything beyond that comes from the abstract.
        var opening = $"\"{title}\", published in {journal} ({year}).";

        if (string.IsNullOrWhiteSpace(abstractText)) return opening + " " + NotStated;

        // One sentence, not two, and cut to the specified ceiling.
        //
        // The introduction is specified at 30 to 50 words - it is the orientation, not the
        // summary. This took the first two sentences of the abstract whole, so a clinical trial
        // with a long title and two long opening sentences produced 98 words, and the reader met
        // the same ground again under the five headings that follow.
        var opened = GetSentenceRange(abstractText, 0, 1);
        if (string.IsNullOrWhiteSpace(opened)) return opening + " " + NotStated;

        return TrimmedToWords($"{opening} The authors open: \"{opened.Trim()}\"", MostIntroductionWords);
    }

    /// <summary>The ceiling the introduction is specified at.</summary>
    public const int MostIntroductionWords = 50;

    /// <summary>
    /// An introduction cut to the specified ceiling, whoever wrote it.
    /// </summary>
    /// <remarks>
    /// Applied to the model's answer as well as the template's. The prompt asks for 30 to 50
    /// words and the model mostly obliges; a specification that is only asked for is one the
    /// product does not actually have.
    /// </remarks>
    public static string WithinIntroductionLength(string? introduction) =>
        TrimmedToWords((introduction ?? string.Empty).Trim(), MostIntroductionWords);

    /// <summary>
    /// Cuts to a word count, on a word boundary, marking that it was cut.
    /// </summary>
    /// <remarks>
    /// The ellipsis matters: a sentence that stops mid-thought with no mark reads as though the
    /// paper said only that much. A reader should be able to tell a cut from a conclusion.
    /// </remarks>
    private static string TrimmedToWords(string text, int most)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= most) return text;

        return string.Join(" ", words[..most]).TrimEnd(',', ';', ':', '"') + "...";
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
        // Year <= 0 is unreachable - default(DateTime) is year 1 - so this guard never fired.
        if (paper.PublishedAt == default) return "Publication date not recorded";

        // One definition, shared with the ranking that uses it. This one read the year alone,
        // so anything older than fifteen years was "Foundational" whether or not a soul had
        // cited it - which is most of what gets published. Being old is not the same as being
        // the thing a field was built on, and saying so about an uncited paper is the kind of
        // claim this product exists not to make.
        return ResearchRanking.EraOf(paper) switch
        {
            null => "Publication date not recorded",
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
