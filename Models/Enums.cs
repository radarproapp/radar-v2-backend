namespace RadarV2.Models;

public enum PersonaType
{
    Student,
    Graduate,
    YoungProfessional,
    Entrepreneur,
    Researcher
}

public enum ContentType
{
    Article,
    Podcast,
    Video,
    ResearchPaper,
    Book,
    Documentation,
    Framework,
    PolicyPaper,
    Essay
}

public enum ContentLayer
{
    // Original layers
    Policy,
    Academic,
    Ideas,
    Learning,
    Video,
    Skills,

    // Interest domain layers
    RealEstate,
    Law,
    Literature,
    Transportation,
    Gaming,
    Art,
    History,
    Sports,
    Music,
    Film,
    Science,
    Environment,
    Travel,
    Medicine,
    Fashion,
    Lifestyle,
    Faith,
    Philosophy,
    Education,

    // Sector layers
    Finance,
    Energy,
    Agriculture,
    Industry,
    Career
}

public enum OpportunityType
{
    Job,
    Internship,
    Scholarship,
    GraduateProgramme,
    ResearchGrant,
    Competition,
    StartupAccelerator,
    Fellowship,
    RemoteJob,
    Event,
    Conference,
    Workshop,
    Training,
    Bootcamp,
    Funding,

    // Appended rather than filed alphabetically: these are persisted by value, so inserting one
    // in the middle would silently relabel every opportunity already stored.
    //
    // Both are named in the spec and neither existed, so a seminar was filed as an Event and a
    // job fair had nowhere to go at all - which means nobody could filter for either, and the
    // persona scoring could not say they suit anybody.
    Seminar,
    JobFair,

    // Two of the eight families had no type that could land in them, so Social Impact and
    // Business & Institutional were offerable on the onboarding question and structurally
    // unfillable - a person would choose one, Radar would have asked and been answered, and the
    // feed would be empty for ever rather than until something was ingested.
    //
    // Mentorship, Award and CallForPapers are named categories in the taxonomy that were being
    // filed as something else, which meant nobody could filter for them either.
    Volunteering,
    Tender,
    Mentorship,
    Award,
    CallForPapers
}

public enum OpportunityTrackingStatus
{
    None,
    Saved,
    Interested,
    Applied,
    NotRelevant,
    Accepted,
    Rejected,

    /// <summary>
    /// The person could not apply, as opposed to not wanting to.
    /// </summary>
    /// <remarks>
    /// Appended because the brief names it and the enum collapsed it into NotRelevant, which
    /// teaches the engine the opposite lesson. "I do not want this" is a reason to show less of
    /// the subject; "I cannot get this" is a reason to show the same subject at a level the
    /// person can actually reach. Treating the second as the first slowly removes somebody's
    /// field from their own feed for the crime of being early in it.
    ///
    /// It is also the signal that tells us an eligibility rule is wrong, which nothing else can:
    /// an opportunity Radar called a match and a person marked ineligible is a match Radar got
    /// wrong, and it says so.
    /// </remarks>
    NotEligible
}

public enum InterestCategory
{
    Technology,
    Business,
    ResearchAndAcademia,
    ProfessionalFields,
    CreativeFields
}

public enum NavigatorCardType
{
    Learn,
    Read,
    Watch,
    Listen,
    Apply,
    Build
}

/// <summary>
/// How far along somebody is professionally, as they describe themselves.
/// </summary>
/// <remarks>
/// Ordered, for the same reason QualificationLevel is: an opportunity asking for a senior
/// candidate is a floor to compare against, not a label to match. It is what stops an
/// AI-interested director being shown a graduate internship and an AI-interested graduate being
/// shown a head-of-department role - both match "AI", and only one of them should see each.
///
/// "Entrepreneur / Founder" is offered beside these on the same question and is deliberately not
/// a member. It is not more senior than a director or less than a manager; it is a different
/// axis, and putting it here would make every comparison wrong in one direction or the other -
/// which is precisely what a professional certification did to the qualification ladder.
/// UserProfile.IsFounder holds it instead.
///
/// Appended to, never reordered: these persist by value.
/// </remarks>
public enum Seniority
{
    Entry,
    Mid,
    Senior,
    Manager,
    Director

    // No NotStated member, deliberately, and this is the third time this lesson has been learnt
    // on this model. A zero meaning "nobody said" sits BELOW Entry on an ordered enum, so the
    // obvious floor check - person >= required - reads an unanswered profile as sub-entry-level
    // and excludes it from everything. A ceiling check reads it as junior enough for anything.
    // Silence cannot be a rung.
    //
    // UserProfile.SeniorityLevel is nullable, so null is the single way to say nothing, which is
    // what QualificationLevel does and what SourceAccess had to be fixed to do. Seniority.Meets
    // below is how a floor should be checked; a raw >= is the bug.
}

/// <summary>Comparing a person against what an opportunity asks for.</summary>
public static class SeniorityRule
{
    /// <summary>
    /// Whether somebody clears a stated floor.
    /// </summary>
    /// <remarks>
    /// Exists so no caller writes <c>person >= floor</c> against a nullable enum and gets the
    /// wrong answer for everybody who has not answered. Nothing stated on either side is not a
    /// failure: an opportunity that names no floor is open on this axis, and a person who has not
    /// said is somebody Radar has not asked yet, not somebody who falls short.
    ///
    /// Not stated by the person, against a floor that was stated, is the one genuinely uncertain
    /// case. It returns true - shown rather than hidden - because the alternative is an empty
    /// feed for every profile that skipped one question, and because the honest fix for not
    /// knowing is to ask, which is what the onboarding question is for.
    /// </remarks>
    public static bool Meets(Seniority? person, Seniority? floor) =>
        floor is null || person is null || person >= floor;

    /// <summary>Whether somebody is inside a stated ceiling, for things aimed at a level.</summary>
    /// <remarks>
    /// A graduate scheme that says "no more than mid-level" is excluding a director on purpose,
    /// and that is a real rule rather than a preference. Same treatment of silence.
    /// </remarks>
    public static bool Within(Seniority? person, Seniority? ceiling) =>
        ceiling is null || person is null || person <= ceiling;
}
