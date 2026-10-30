using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// What each kind of user is here for, as subject matter rather than as a kind of opportunity.
/// </summary>
/// <remarks>
/// The spec asks for a feed prioritised by category: a student should meet scholarships,
/// internships and research developments near the top; a graduate should meet graduate schemes,
/// entry-level roles and in-demand certifications. <see cref="OpportunityFit"/> already does this
/// for adverts, where the type of the thing says most of it. Nothing did it for reading.
///
/// Persona reached the content scorer through one route only: a flat five points when an item
/// carried a <c>PersonaImpact</c> entry for the reader's persona. That dictionary is written by
/// the AI enricher and by nothing else, so on any item the enricher has not reached — which is
/// most of them, and all of them while the key is out of credit — a student and a professional
/// with the same interests got byte-identical rankings. The category was collected at signup and
/// then had no effect on the feed it was collected for.
///
/// Stated answers still win. This is weighted below a goal (30) and below a named problem (20),
/// because the category is a box somebody ticked and the goal is a sentence they wrote. It sits
/// above a general interest, because "scholarship" is what a student came for even when the
/// subject is one they never listed.
/// </remarks>
public static class PersonaPriorities
{
    /// <summary>What a category being right is worth against what the reader typed.</summary>
    /// <remarks>
    /// Below a stated interest, which is ten. It was twelve, on the reasoning that a
    /// scholarship is what a student came for even on a subject they never listed - and the
    /// use-case suite showed what that actually does: a scholarship notice for software
    /// engineering students outranked news in a nursing student's own field, for every student
    /// in the corpus and every career in it.
    ///
    /// The spec settles it. Category lists are "starting priorities, not restrictions", and
    /// "the user's actual interests and goals must influence what appears in their feed". A
    /// priority that outranks the subject somebody typed is a restriction wearing another name.
    /// Eight still lifts the things a stage is for above everything that matched nothing.
    /// </remarks>
    public const double Weight = 8;

    /// <summary>The dimension name, as it appears in a matched signal.</summary>
    public const string Dimension = "Category";

    /// <summary>
    /// How much a term may grow at the end and still be the same term.
    /// </summary>
    /// <remarks>
    /// Three letters covers the endings a headline actually uses - scholarship/scholarships,
    /// internship/internships, bursary/bursaries - and stops short of a different word.
    /// The same bound, for the same reason, as <see cref="DomainVocabulary"/>.
    ///
    /// It is not free. "grant" reaches "granted", "award" reaches "awarded" and "promotion"
    /// reaches "promotional" inside it, which is why none of those three is listed on its own.
    /// </remarks>
    private const int MostATermMayGrow = 3;

    /// <summary>
    /// Whether the text is about this term, as opposed to merely containing its letters.
    /// </summary>
    /// <remarks>
    /// The whole phrase, bounded at both ends. The scorer's general matcher splits a phrase into
    /// keywords, drops anything under four letters, and then asks whether the text contains each
    /// of them anywhere at all. That is right for a subject a user typed and wrong for an
    /// authored list, because it found every one of these in text about something else:
    /// "graduate job" reduced to the single word "graduate" and fired on "undergraduate
    /// enrolment"; "seed round" fired on "seed yields improve around smallholder farms";
    /// "business grant" fired on "migrant workers are being granted permits"; "industry report"
    /// is a literal substring of "industry reported record profits".
    ///
    /// Each of those was worth twelve points and, worse, was then printed to the reader as the
    /// reason the item was in front of them. These terms are written here rather than typed by a
    /// user, so they can be required to appear as themselves.
    /// </remarks>
    public static bool Mentions(string? text, string term)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term)) return false;

        return PatternFor(term).IsMatch(text);
    }

    /// <summary>
    /// The compiled pattern for a term, built once.
    /// </summary>
    /// <remarks>
    /// This interpolated the pattern on every call and handed it to the static Regex.IsMatch,
    /// which caches fifteen. The longest list here holds twenty-six terms, so scoring a single
    /// item evicted the cache and recompiled every term after it - and the research library
    /// scores two hundred candidates per request. Thousands of regex constructions a call, for
    /// twenty-six patterns that never change.
    /// </remarks>
    private static Regex PatternFor(string term) =>
        Compiled.GetOrAdd(term, t => new Regex(
            $@"\b{Regex.Escape(t)}[a-z]{{0,{MostATermMayGrow}}}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100)));

    private static readonly ConcurrentDictionary<string, Regex> Compiled = new(StringComparer.Ordinal);

    /// <summary>
    /// The term this text is about, or null if none of them.
    /// </summary>
    /// <remarks>
    /// The longest match wins, so an item about a "graduate trainee programme" is reported as
    /// that rather than as whichever shorter term happened to be listed first.
    /// </remarks>
    public static string? Matched(string? text, PersonaType persona) =>
        For(persona)
            .Where(term => Mentions(text, term))
            .OrderByDescending(term => term.Length)
            .FirstOrDefault();

    private static readonly Dictionary<PersonaType, string[]> ByStage = new()
    {
        // Still studying. Named in the spec: scholarships and bursaries, internships, research
        // developments, academic competitions, fellowships and exchange programmes, relevant
        // courses and certifications, education policy updates, student entrepreneurship, and
        // academic conferences and workshops.
        [PersonaType.Student] =
        [
            "scholarship",
            "bursary",
            "bursaries",
            "internship",
            "undergraduate research",
            "academic competition",
            "student competition",
            "hackathon",
            "research fellowship",
            "fellowship programme",
            "fellowship application",
            "student exchange",
            "exchange programme for students",
            "professional certification",
            "skills certification",
            "certification course",
            "online course",
            "education policy",
            "student entrepreneurship",
            "academic conference",
            "call for papers",
        ],

        // Finished studying, not yet established. The spec's list: graduate jobs and internships,
        // entry-level opportunities, graduate trainee programmes, scholarships and funded
        // postgraduate programmes, in-demand skills and certifications, industry developments,
        // professional networking, competitions and entrepreneurship, career development.
        [PersonaType.Graduate] =
        [
            "graduate job",
            "graduate scheme",
            "graduate trainee",
            "graduate programme",
            "graduate program",
            "graduate recruitment",
            "entry-level",
            "entry level",
            "internship",
            "scholarship",
            "postgraduate",
            "professional certification",
            "skills certification",
            "certification course",
            "in-demand",
            "industry trend",
            "industry outlook",
            "professional networking",
            "career development",
            "recruitment drive",
            "entrepreneurship",
        ],

        // Established and working. The spec's list: industry developments, market and business
        // intelligence, relevant funding, grants, contracts and tenders, professional
        // certifications, leadership and career advancement, new technologies and industry
        // practices, research findings and emerging trends, conferences and professional events,
        // and strategic partnerships.
        //
        // "promotion" is deliberately absent, though career advancement is the spec's own
        // wording for it. Bounded growth still reaches "promotional", so every festive-season
        // advertising story would have scored as career news.
        [PersonaType.YoungProfessional] =
        [
            "industry development",
            "industry trend",
            "industry outlook",
            "industry best practice",
            "market intelligence",
            "market outlook",
            "grant funding",
            "grant application",
            "funding opportunity",
            "call for tender",
            "tender notice",
            "procurement notice",
            "contract award notice",
            "request for proposal",
            "professional certification",
            "leadership development programme",
            "executive leadership",
            "management development",
            "career advancement",
            "career progression",
            "emerging technology",
            "emerging trend",
            "research finding",
            "professional conference",
            "industry conference",
            "strategic partnership agreement",
        ],

        [PersonaType.Entrepreneur] =
        [
            "startup funding",
            "seed round",
            "venture capital",
            "startup accelerator",
            "accelerator programme",
            "accelerator program",
            "business incubator",
            "pitch competition",
            "business grant",
            "business registration",
            "market expansion",
            "small business policy",
            "customer acquisition",
        ],

        // "publication" is absent for the same reason as "promotion": a newspaper is a
        // publication, and "the publication reported" is not research news.
        [PersonaType.Researcher] =
        [
            "research grant",
            "research funding",
            "call for papers",
            "academic conference",
            "research fellowship",
            "fellowship programme",
            "fellowship application",
            "peer-reviewed",
            "peer review process",
            "postdoctoral",
            "preprint",
            "research collaboration",
        ],
    };

    /// <summary>
    /// The subject matter this category came for. Empty for a category nobody wrote a list for,
    /// which costs that reader nothing: the dimension simply does not fire.
    /// </summary>
    public static IReadOnlyList<string> For(PersonaType persona) =>
        ByStage.TryGetValue(persona, out var terms) ? terms : [];
}
