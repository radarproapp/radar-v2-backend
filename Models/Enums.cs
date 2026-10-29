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
    JobFair
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
