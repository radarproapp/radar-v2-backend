using System.Text.RegularExpressions;
using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Reading the criteria a posting actually states, and recording nothing else.
/// </summary>
/// <remarks>
/// Both ingesters left <see cref="Opportunity.Eligibility"/> empty on purpose, and the reason
/// still stands: "inventing them from a summary is how people end up applying for things they do
/// not qualify for." The consequence was that the whole eligibility half of the engine had no
/// input - every posting read as unchecked, and a comparator with nothing to compare is a
/// comparator that decides nothing.
///
/// So this extracts rather than infers. Every pattern below matches an explicit statement, and
/// anything short of one is left absent, because absent means "not stated" everywhere else in the
/// model and that is the honest reading. A posting that says "the ideal candidate will be
/// experienced" states nothing this can use, and the right answer is to record nothing.
///
/// Deliberately conservative in a specific direction. A missed criterion costs somebody a listing
/// they would have been shown anyway - the verdict falls back to Unchecked or CannotTell, both of
/// which still display. A wrong criterion costs them an opportunity they qualify for, silently.
/// Those are not symmetrical, so every pattern here would rather match nothing than guess.
///
/// Reading a posting is not republishing it. These run over text Radar is permitted to fetch
/// regardless of whether it may store the description, the same argument the deadline and region
/// inference already rest on: a closing date is a fact about the opportunity, not the source's
/// expression of it.
/// </remarks>
public static partial class EligibilityExtractor
{
    /// <summary>
    /// What a posting says about who may apply.
    /// </summary>
    /// <remarks>
    /// Returns an Eligibility whose <see cref="Eligibility.WasStated"/> is false when nothing was
    /// found, which is exactly what the callers already store - so a posting this cannot read is
    /// no worse off than before.
    /// </remarks>
    public static Eligibility From(string? title, string? body)
    {
        // A full stop between them. Joined with a space, a match ran from the end of the title
        // into the start of the body - "Massive Recruitment: Up to 25 Positions" followed by any
        // body text produced an age limit of 25.
        //
        // Capped at the length that actually gets stored, which is also what bounds the cost: the
        // RSS path passes the whole body, and 198KB of it took a regex past its timeout - which
        // escaped the entry loop and blocked every older entry in that feed on every later run.
        var text = Shorten($"{title}. {body}", 2000);
        var eligibility = new Eligibility { Source = EligibilitySource.Extracted };

        if (string.IsNullOrWhiteSpace(text)) return eligibility;

        eligibility.MinimumQualification = ReadQualification(text);
        ReadExperience(text, eligibility);
        ReadAge(text, eligibility);
        eligibility.Nationalities = ReadNationalities(text);

        return eligibility;
    }

    private static string Shorten(string value, int most) =>
        value.Length <= most ? value : value[..most];

    // ── qualifications ───────────────────────────────────────────────────────

    /// <summary>
    /// The lowest qualification a posting asks for.
    /// </summary>
    /// <remarks>
    /// Matched only where the phrasing marks it as a floor - "minimum", "at least", "or higher",
    /// "or above", "required". A description that merely mentions a master's degree is often
    /// describing the programme rather than the applicant, and "scholarship for a master's
    /// degree" asks for a bachelor's, not a master's. Taking every mention as a floor would read
    /// that one exactly backwards.
    ///
    /// Where several are stated the lowest wins, because a posting saying "HND or BSc" is open to
    /// both and the floor is the lower rung.
    /// </remarks>
    private static QualificationLevel? ReadQualification(string text)
    {
        // Abbreviation dots are not sentence ends. The windows below stop at a full stop, so the
        // dot inside "B.Sc" cut "HND or B.Sc required" in half and the HND half never matched -
        // recording a bachelor's floor on a posting that names HND first, and excluding exactly
        // the people it was open to.
        var flat = Abbreviations().Replace(text, m => m.Value.Replace(".", ""));

        // Nothing is read out of a sentence that denies it. "A university degree is not
        // required" recorded a bachelor's requirement; "no B.Sc required" recorded one too.
        // Both are the posting saying the opposite of what was stored.
        var found = new List<QualificationLevel>();

        foreach (var (pattern, level) in Qualifications)
        {
            foreach (Match match in Regex.Matches(flat, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
            {
                if (Denied(flat, match)) continue;
                if (AwardedRatherThanRequired(flat, match)) continue;

                found.Add(level);
                break;
            }
        }

        return found.Count > 0 ? found.Min() : null;
    }

    /// <summary>Whether the sentence around a match denies it.</summary>
    /// <remarks>
    /// Checked on the clause the match sits in rather than the whole text, so a denial elsewhere
    /// in a long posting does not suppress a real requirement.
    /// </remarks>
    private static bool Denied(string text, Match match) =>
        Negation().IsMatch(Clause(text, match));

    /// <summary>
    /// Whether the degree is what the programme gives rather than what it asks for.
    /// </summary>
    /// <remarks>
    /// The failure this extractor was most at risk of, and the tests only covered the easy half.
    /// "Applicants must have been admitted to a master degree programme" carries a requirement
    /// cue and is still describing the award - so a bachelor's-holding graduate was marked
    /// ineligible for the scholarship aimed at them.
    /// </remarks>
    private static bool AwardedRatherThanRequired(string text, Match match) =>
        Awarded().IsMatch(Clause(text, match));

    /// <summary>The clause a match sits in, bounded by ordinary sentence punctuation.</summary>
    private static string Clause(string text, Match match)
    {
        var from = text.LastIndexOfAny(['.', ';', '\n'], Math.Max(0, match.Index - 1)) + 1;
        var toIndex = text.IndexOfAny(['.', ';', '\n'], match.Index + match.Length);
        var to = toIndex < 0 ? text.Length : toIndex;

        return text[from..to];
    }

    /// <remarks>
    /// "No more than" and "not less than" are bounds, not denials, and the distinction matters:
    /// "a bachelor's degree, no more than 2 years experience" is one clause carrying a real
    /// requirement and a real ceiling, and vetoing the whole thing on the word "no" threw the
    /// qualification away.
    /// </remarks>
    [GeneratedRegex(@"\b(?:no|not)\b(?!\s+(?:more|less|fewer)\s+than)|\bwithout\b|\bnever\b|n(?:'|')t\b", RegexOptions.IgnoreCase, 200)]
    private static partial Regex Negation();

    [GeneratedRegex(@"\b(?:programme|program|scholarship|fellowship|admission|admitted|enrol(?:led|ment)?|funding|award(?:ed|s)?|towards?|study|studies)\b", RegexOptions.IgnoreCase, 200)]
    private static partial Regex Awarded();

    [GeneratedRegex(@"\b(?:B\.Sc|M\.Sc|Ph\.D|B\.A|M\.A|B\.Eng|M\.Eng)\b", RegexOptions.IgnoreCase, 200)]
    private static partial Regex Abbreviations();

    /// <summary>
    /// Each rung, and the phrasings that mark it as a requirement rather than a mention.
    /// </summary>
    /// <remarks>
    /// The qualifying words are the whole point. Without them "a PhD programme" reads as "needs a
    /// PhD", which excludes exactly the people it is for.
    /// </remarks>
    private static readonly (string Pattern, QualificationLevel Level)[] Qualifications =
    [
        (@"(?:minimum|at least|must (?:have|possess)|requires?|required)[^.;:,]{0,18}\bO(?:'|')?\s?level\b", QualificationLevel.SecondarySchool),
        (@"\bO(?:'|')?\s?level\b[^.;:,]{0,15}(?:required|or (?:higher|above)|minimum)", QualificationLevel.SecondarySchool),
        (@"(?:minimum|at least|must (?:have|possess)|requires?|required)[^.;:,]{0,18}\bNCE\b", QualificationLevel.Nce),
        (@"\bNCE\b[^.;:,]{0,15}(?:required|or (?:higher|above)|minimum)", QualificationLevel.Nce),
        (@"(?:minimum|at least|must (?:have|possess)|requires?|required)[^.;:,]{0,18}\bOND\b", QualificationLevel.Ond),
        (@"\bOND\b[^.;:,]{0,15}(?:required|or (?:higher|above)|minimum)", QualificationLevel.Ond),
        (@"(?:minimum|at least|must (?:have|possess)|requires?|required)[^.;:,]{0,18}\bHND\b", QualificationLevel.Hnd),
        (@"\bHND\b[^.;:,]{0,15}(?:required|or (?:higher|above)|minimum)", QualificationLevel.Hnd),
        (@"(?:minimum|at least|must (?:have|possess)|requires?|required)[^.;:,]{0,18}(?:bachelor(?:'|')?s?|b\.?sc|first degree|university degree)", QualificationLevel.Bachelors),
        (@"(?:bachelor(?:'|')?s?|b\.?sc|first degree|university degree)[^.;:,]{0,15}(?:required|or (?:higher|above)|minimum)", QualificationLevel.Bachelors),
        (@"(?:minimum|at least|must (?:have|possess)|requires?|required)[^.;:,]{0,18}(?:master(?:'|')?s?|m\.?sc|postgraduate degree)", QualificationLevel.Masters),
        (@"(?:master(?:'|')?s?|m\.?sc)[^.;:,]{0,15}(?:required|or (?:higher|above)|minimum)", QualificationLevel.Masters),
        (@"(?:minimum|at least|must (?:have|possess)|requires?|required)[^.;:,]{0,18}(?:ph\.?d|doctorate|doctoral degree)", QualificationLevel.Doctorate),
        (@"(?:ph\.?d|doctorate)[^.;:,]{0,15}(?:required|or (?:higher|above)|minimum)", QualificationLevel.Doctorate),
    ];

    // ── experience ───────────────────────────────────────────────────────────

    /// <summary>
    /// Years of experience, in whichever direction the posting states.
    /// </summary>
    /// <remarks>
    /// The ceiling is read as carefully as the floor, because it is the one that matters for the
    /// graduate programmes this product is mostly about - "no more than two years" is the rule
    /// that makes a scheme a graduate scheme.
    ///
    /// A range like "3-5 years" sets both. Anything above thirty is ignored: it is far more
    /// likely to be a year, a salary or a street number that happened to sit beside the word.
    /// </remarks>
    private static void ReadExperience(string text, Eligibility eligibility)
    {
        var floor = MinimumExperience().Match(text);
        if (floor.Success && int.TryParse(floor.Groups[1].Value, out var least) && least is > 0 and <= 30)
            eligibility.MinimumYearsExperience = least;

        var ceiling = MaximumExperience().Match(text);
        if (ceiling.Success && int.TryParse(ceiling.Groups[1].Value, out var most) && most is > 0 and <= 30)
            eligibility.MaximumYearsExperience = most;

        // Only when neither bound was read on its own. The range used to overwrite both, so
        // "at least 8 years experience; the scheme offers 1-2 years of experience to new joiners"
        // destroyed the real floor of eight and invented a ceiling of two - excluding precisely
        // the senior candidate the advert was for. Where two readings disagree the design says
        // record nothing, and that is what leaving the stated bound alone amounts to.
        if (eligibility.MinimumYearsExperience is not null || eligibility.MaximumYearsExperience is not null)
            return;

        var range = ExperienceRange().Match(text);
        if (range.Success
            && int.TryParse(range.Groups[1].Value, out var from)
            && int.TryParse(range.Groups[2].Value, out var to)
            && from <= to && to <= 30)
        {
            eligibility.MinimumYearsExperience = from;
            eligibility.MaximumYearsExperience = to;
        }
    }

    /// <remarks>
    /// "Experience" has to follow closely and the bare "over" is gone. The old window let thirty
    /// characters sit between, so "the fellowship runs for a minimum of 2 years and offers
    /// hands-on experience" recorded a two-year floor, and "at least 3 years of experience
    /// working with us is typical" recorded three.
    /// </remarks>
    [GeneratedRegex(
        @"(?:minimum of|at least|a minimum of|not less than)\s+(\d{1,2})\+?\s*(?:years?|yrs?)\s*(?:of\s+)?(?:relevant\s+|work\s+|professional\s+|hands-on\s+|post-NYSC\s+)?experience",
        RegexOptions.IgnoreCase, 200)]
    private static partial Regex MinimumExperience();

    [GeneratedRegex(
        @"(?:no more than|not more than|less than|under|up to|maximum of|at most)\s+(\d{1,2})\s*(?:years?|yrs?)\s*(?:of\s+)?(?:relevant\s+|work\s+|professional\s+|post-NYSC\s+)?experience",
        RegexOptions.IgnoreCase, 200)]
    private static partial Regex MaximumExperience();

    [GeneratedRegex(
        @"\b(\d{1,2})\s*(?:-|–|to)\s*(\d{1,2})\s*(?:years?|yrs?)\s*(?:of\s+)?(?:relevant\s+|work\s+|professional\s+)?experience",
        RegexOptions.IgnoreCase, 200)]
    private static partial Regex ExperienceRange();

    // ── age ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// An age range, which is the one criterion nobody can acquire.
    /// </summary>
    /// <remarks>
    /// Bounded to a plausible human range on the way in. "Under 30" beside a figure like 2030 is
    /// a year, and an age bound Radar invented is worse than none - it is the criterion that can
    /// never be satisfied by trying harder.
    /// </remarks>
    private static void ReadAge(string text, Eligibility eligibility)
    {
        var range = AgeRange().Match(text);
        if (range.Success
            && int.TryParse(range.Groups[1].Value, out var from)
            && int.TryParse(range.Groups[2].Value, out var to)
            && Plausible(from) && Plausible(to) && from <= to)
        {
            eligibility.MinimumAge = from;
            eligibility.MaximumAge = to;
            return;
        }

        if (Captured(MaximumAge().Match(text)) is { } oldest && Plausible(oldest))
            eligibility.MaximumAge = oldest;

        if (Captured(MinimumAge().Match(text)) is { } youngest && Plausible(youngest))
            eligibility.MinimumAge = youngest;
    }

    private static bool Plausible(int age) => age is >= 10 and <= 75;

    /// <summary>The number from whichever branch of the pattern matched.</summary>
    /// <remarks>
    /// Both age patterns have two alternatives - one requiring an age cue, one requiring the word
    /// "years" - so the digits land in group 1 or group 2 depending which fired. Reading group 1
    /// alone silently stopped extracting anything phrased the second way.
    /// </remarks>
    private static int? Captured(Match match)
    {
        if (!match.Success) return null;

        for (var group = 1; group < match.Groups.Count; group++)
            if (match.Groups[group].Success && int.TryParse(match.Groups[group].Value, out var value))
                return value;

        return null;
    }

    /// <remarks>
    /// Word-bounded, because without it "average", "package", "coverage", "percentage" and
    /// "language" all ended in "age" and fired. "Average 40-45 hours per week" in a graduate
    /// advert recorded an age range of 40 to 45 and hid it from every graduate.
    /// </remarks>
    [GeneratedRegex(@"\b(?:aged|age)\b\s*(?:between\s*)?(\d{2})\s*(?:-|–|to|and)\s*(\d{2})", RegexOptions.IgnoreCase, 200)]
    private static partial Regex AgeRange();

    /// <remarks>
    /// A unit is mandatory, or an age cue is. The first version ended in "|$|\b", which matches
    /// immediately after any two digits - so "not more than 14 days from the date of this
    /// publication" recorded an age limit of fourteen, "up to 20 hours per week" recorded twenty,
    /// and both hid the posting from everyone who had given a date of birth.
    /// </remarks>
    [GeneratedRegex(
        @"(?:(?:maximum age of|up to the age of|under the age of|below the age of)\s*(\d{2})\b|(?:under|below|younger than|not (?:more than|older than))\s*(\d{2})\s*years?(?:\s*old)?\b)",
        RegexOptions.IgnoreCase, 200)]
    private static partial Regex MaximumAge();

    /// <remarks>
    /// An age cue is required rather than optional. "Over 20 years" and "at least 15 years" are
    /// overwhelmingly about a company's history on a job board - "with over 15 years of
    /// experience in the Nigerian market" recorded an age floor of fifteen and an experience
    /// floor of fifteen, and excluded both a graduate and a mid-career applicant from an ordinary
    /// advert.
    /// </remarks>
    [GeneratedRegex(
        @"(?:minimum age of|(?:at least|over|older than|above)\s*the age of)\s*(\d{2})\b|(?:at least|older than|above)\s*(\d{2})\s*years?\s*(?:old|of age)",
        RegexOptions.IgnoreCase, 200)]
    private static partial Regex MinimumAge();

    // ── nationality ──────────────────────────────────────────────────────────

    /// <summary>
    /// Nationalities a posting says it is open to.
    /// </summary>
    /// <remarks>
    /// The narrowest extraction here, and on purpose. The brief singles this one out - "do not
    /// infer nationality eligibility merely because a programme is described as international" -
    /// and a wrongly recorded nationality is the criterion most likely to hide something from
    /// exactly the person it was meant for.
    ///
    /// So only an explicit restriction counts: "open to Nigerian nationals", "Nigerian citizens
    /// only", "applicants must be Ghanaian". A posting that merely mentions Nigeria - which is
    /// most of them, on a Nigerian job board - states nothing about nationality at all.
    /// </remarks>
    private static List<string> ReadNationalities(string text)
    {
        var found = new List<string>();

        foreach (Match match in Nationality().Matches(text))
        {
            var clause = Clause(text, match);

            // "This programme is not open to Nigerian nationals" recorded it as open to
            // Nigerians only - the posting inverted, and the one group excluded became the one
            // group allowed.
            if (Negation().IsMatch(clause)) continue;

            // Every demonym in the clause, not only the one the cue happened to touch. "Open to
            // Nigerian citizens, Ghanaian citizens and Kenyan citizens" recorded Nigerian alone,
            // which hid the scholarship from the two groups it explicitly named.
            var words = AnyDemonym().Matches(clause).Select(w => w.Value).ToList();

            for (var i = 0; i < words.Count; i++)
            {
                // The pair first, so "Sierra Leonean" is not read as "Sierra" and discarded. The
                // single-token scan it replaces made every multi-word demonym unreachable.
                var pair = i + 1 < words.Count ? $"{words[i]} {words[i + 1]}" : null;

                if (pair is not null && Demonyms.TryGetValue(pair, out var both))
                {
                    if (!found.Contains(both)) found.Add(both);
                    i++;
                    continue;
                }

                if (Demonyms.TryGetValue(words[i], out var one) && !found.Contains(one))
                    found.Add(one);
            }
        }

        return found;
    }

    [GeneratedRegex(@"(?:open to|restricted to|must be|applicants? (?:must be|should be)|only)\s+(?:citizens of\s+|nationals of\s+)?([A-Z][a-z]+)(?:\s+(?:nationals?|citizens?|passport holders?|applicants?|candidates?))", RegexOptions.IgnoreCase)]
    private static partial Regex Nationality();

    [GeneratedRegex(@"[A-Za-z]+", RegexOptions.IgnoreCase, 200)]
    private static partial Regex AnyDemonym();

    /// <summary>
    /// The demonyms worth recognising, and their canonical spelling.
    /// </summary>
    /// <remarks>
    /// A closed list rather than anything that captures a capitalised word. An open capture would
    /// read "open to Graduate nationals" or "must be Senior candidates" as nationalities, and
    /// every one of those silently hides the posting from everybody.
    ///
    /// Matched to how the onboarding profile stores them, so the comparison in EligibilityCheck
    /// is against the same spelling a person would have given.
    /// </remarks>
    private static readonly Dictionary<string, string> Demonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nigerian"] = "Nigerian",
        ["Ghanaian"] = "Ghanaian",
        ["Kenyan"] = "Kenyan",
        ["Ugandan"] = "Ugandan",
        ["Tanzanian"] = "Tanzanian",
        ["Rwandan"] = "Rwandan",
        ["Ethiopian"] = "Ethiopian",
        ["Egyptian"] = "Egyptian",
        ["Moroccan"] = "Moroccan",
        ["Senegalese"] = "Senegalese",
        ["Cameroonian"] = "Cameroonian",
        ["Zambian"] = "Zambian",
        ["Zimbabwean"] = "Zimbabwean",
        ["Botswanan"] = "Botswanan",
        ["Namibian"] = "Namibian",
        ["Malawian"] = "Malawian",
        ["Ivorian"] = "Ivorian",
        ["Beninese"] = "Beninese",
        ["Togolese"] = "Togolese",
        ["Liberian"] = "Liberian",
        ["Sierra Leonean"] = "Sierra Leonean",
        ["Gambian"] = "Gambian",
        ["Sudanese"] = "Sudanese",
        ["Somali"] = "Somali",
        ["Angolan"] = "Angolan",
        ["Mozambican"] = "Mozambican",
        ["Congolese"] = "Congolese",
        ["Algerian"] = "Algerian",
        ["Tunisian"] = "Tunisian",
        ["Burkinabe"] = "Burkinabe",
        ["Malian"] = "Malian",
        ["Nigerien"] = "Nigerien",
    };
}
