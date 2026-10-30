using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Whether a kind of opportunity suits the kind of person the learner says they are.
/// </summary>
/// <remarks>
/// Pulled out of the scorer so it can be read and tested as a table, which is what it is. It was
/// a switch expression in the middle of a 160-line method, and the gaps in it were invisible:
/// Graduate appeared nowhere, so every graduate fell to the default while GraduateProgramme -
/// the single most relevant type for one - matched nobody.
///
/// Two rules. A type that genuinely belongs to a stage scores for that stage. And the things
/// anybody can turn up to - a conference, a seminar, a workshop, a job fair - score for everybody,
/// because a student who wants to go to a job fair and a professional who wants to go to the same
/// job fair are both right. They scored four points, the no-match default, which put them below
/// the fold for every persona at once.
/// </remarks>
public static class OpportunityFit
{
    /// <summary>
    /// The ones nobody is the wrong person for.
    /// </summary>
    /// <remarks>
    /// Named in the spec: conferences, seminars, workshops, job fairs. Where you are in a career
    /// changes which job you apply for; it does not change whether a workshop in your field is
    /// worth a Saturday.
    /// </remarks>
    public static readonly OpportunityType[] OpenToAnyone =
    [
        OpportunityType.Conference,
        OpportunityType.Seminar,
        OpportunityType.Workshop,
        OpportunityType.Training,
        OpportunityType.JobFair,
        OpportunityType.Event,
    ];

    /// <summary>
    /// What each kind of person is looking for, in the words of the type list.
    /// </summary>
    private static readonly Dictionary<PersonaType, OpportunityType[]> ForStage = new()
    {
        [PersonaType.Student] =
        [
            OpportunityType.Internship,
            OpportunityType.Scholarship,
            OpportunityType.Bootcamp,
            OpportunityType.Competition,
            // "Fellowships and exchange programmes", in the spec's list for this stage. It was
            // here for a graduate and not for a student, so an undergraduate exchange scored the
            // no-signal four.
            OpportunityType.Fellowship,
        ],

        // Between studying and working, which is the stage with the most types of its own: the
        // graduate schemes, the first job, and the fellowships and traineeships in between.
        [PersonaType.Graduate] =
        [
            OpportunityType.GraduateProgramme,
            OpportunityType.Internship,
            OpportunityType.Fellowship,
            OpportunityType.Job,
            OpportunityType.Scholarship,
            OpportunityType.Bootcamp,
            // "Competitions and entrepreneurship opportunities", in the spec's list for this
            // stage, and scored for a student but not for the graduate a year later.
            OpportunityType.Competition,
        ],

        [PersonaType.YoungProfessional] =
        [
            OpportunityType.Job,
            OpportunityType.RemoteJob,
            OpportunityType.Fellowship,
        ],

        [PersonaType.Entrepreneur] =
        [
            OpportunityType.StartupAccelerator,
            OpportunityType.Funding,
            OpportunityType.ResearchGrant,
            OpportunityType.Competition,
        ],

        [PersonaType.Researcher] =
        [
            OpportunityType.ResearchGrant,
            OpportunityType.Fellowship,
            OpportunityType.Funding,
            OpportunityType.Conference,
        ],
    };

    /// <summary>Where someone starts when the type says nothing about them either way.</summary>
    public const int NoSignal = 4;

    /// <summary>What a type being right for this stage is worth.</summary>
    public const int Stage = 10;

    /// <summary>And what a thing anybody can attend is worth.</summary>
    public const int Anyone = 7;

    /// <summary>
    /// How much this type being what it is says about the fit.
    /// </summary>
    /// <remarks>
    /// Never zero. A type nobody wrote a rule for is not evidence against an opportunity, and the
    /// rest of the scoring - the field, the interests, the goals - is what decides it.
    /// </remarks>
    public static int PointsFor(PersonaType persona, OpportunityType type)
    {
        if (ForStage.TryGetValue(persona, out var wanted) && wanted.Contains(type)) return Stage;

        return OpenToAnyone.Contains(type) ? Anyone : NoSignal;
    }

    /// <summary>
    /// Which of a learner's stated terms this opportunity actually speaks to.
    /// </summary>
    /// <remarks>
    /// By word, not by substring. A plain Contains on a stated interest matched any advert with
    /// those letters anywhere in it: "art" hit every "start date", "AI" hit "said", and the
    /// learner was then told the reason it was shown was their interest in art. MentionsSubject
    /// matches whole words and the forms an advert actually uses - nursing against "nurse" - and
    /// is the same rule the feed applies to the same kind of text.
    ///
    /// Returns the terms rather than a count, because every one of them has to be sayable back to
    /// the reader as the reason this is in front of them.
    /// </remarks>
    public static List<string> Matching(string text, IEnumerable<string?> terms) =>
        terms
            .Where(term => !string.IsNullOrWhiteSpace(term) && DomainVocabulary.MentionsSubject(text, term))
            .Select(term => term!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Whether the type itself is a reason this is in front of them.</summary>
    public static bool IsASignal(PersonaType persona, OpportunityType type) =>
        PointsFor(persona, type) > NoSignal;
}
