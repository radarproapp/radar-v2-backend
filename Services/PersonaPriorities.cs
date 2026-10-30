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
///
/// Terms are stems, deliberately. The matcher asks whether the item's text contains the term, so
/// "scholarship" finds "scholarships" while the reverse finds nothing. Multi-word terms need all
/// of their words, which is what keeps "industry report" from firing on every use of "industry".
/// </remarks>
public static class PersonaPriorities
{
    /// <summary>What a category being right is worth against what the reader typed.</summary>
    public const double Weight = 12;

    /// <summary>The dimension name, as it appears in a matched signal.</summary>
    public const string Dimension = "Category";

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
            "internship",
            "undergraduate research",
            "student competition",
            "academic competition",
            "hackathon",
            "fellowship",
            "exchange programme",
            "exchange program",
            "certification",
            "online course",
            "education policy",
            "student entrepreneurship",
            "academic conference",
            "call for paper",
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
            "entry-level",
            "internship",
            "scholarship",
            "postgraduate",
            "certification",
            "in-demand skill",
            "industry report",
            "industry trend",
            "professional networking",
            "career development",
            "recruitment drive",
            "entrepreneurship",
        ],

        // Established and moving. The spec's part on professionals was not to hand when this was
        // written, so this is the stage read the same way as the two that were specified: what
        // the category is for rather than what the person happens to follow. Worth checking
        // against the written list.
        [PersonaType.YoungProfessional] =
        [
            "professional certification",
            "career advancement",
            "promotion",
            "leadership programme",
            "leadership program",
            "management training",
            "executive education",
            "industry report",
            "industry trend",
            "salary",
            "compensation",
            "remote work",
            "professional networking",
            "upskilling",
            "mentorship",
        ],

        [PersonaType.Entrepreneur] =
        [
            "startup funding",
            "seed round",
            "venture capital",
            "accelerator",
            "incubator",
            "pitch competition",
            "business grant",
            "business registration",
            "market expansion",
            "small business policy",
            "customer acquisition",
            "founder",
        ],

        [PersonaType.Researcher] =
        [
            "research grant",
            "research funding",
            "call for paper",
            "academic conference",
            "fellowship",
            "peer review",
            "postdoctoral",
            "publication",
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
