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
    Rejected
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
    /// <summary>Nobody has said. Never read as meeting a floor.</summary>
    NotStated,

    Entry,
    Mid,
    Senior,
    Manager,
    Director
}
