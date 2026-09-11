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
    RemoteJob
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
