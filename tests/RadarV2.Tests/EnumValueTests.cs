using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// The stored numbers behind the names.
/// </summary>
/// <remarks>
/// Every one of these enums is persisted by value, so the integer is the real schema and the name
/// is a label this codebase happens to use for it. Reordering one, or inserting a member in the
/// middle, silently relabels every document already written: a <c>Scholarship</c> becomes a
/// <c>Training</c> at rest, without an error, a migration or a failing test.
///
/// Four separate comments said "append only" and enforced nothing - a reorder was made
/// deliberately against the suite and all 29 taxonomy tests still passed. This is the file that
/// makes the rule real, so the diff that breaks it cannot be merged without somebody reading this
/// and deciding.
///
/// If a test here fails, the fix is almost never to update the expected list. It is to move the
/// new member to the end. Changing a list below is a data migration, and the number already
/// written in Mongo does not change because the C# did.
/// </remarks>
public class EnumValueTests
{
    [Theory]
    [MemberData(nameof(PersistedEnums))]
    public void PersistedEnumsKeepTheNumbersAlreadyWrittenToTheDatabase(string label, Type type, string[] expected)
    {
        var actual = Enum.GetValues(type)
            .Cast<object>()
            .Select(value => $"{Convert.ToInt32(value)}={Enum.GetName(type, value)}")
            .ToArray();

        var pinned = expected.Select((name, index) => $"{index}={name}").ToArray();

        Assert.True(
            pinned.SequenceEqual(actual),
            $"""
             {label} no longer stores the values it has already written.

             pinned:  {string.Join(", ", pinned)}
             current: {string.Join(", ", actual)}

             Appending a member at the end is safe - add it to the pinned list.
             Anything else rewrites the meaning of documents already in Mongo.
             """);
    }

    public static TheoryData<string, Type, string[]> PersistedEnums() => new()
    {
        {
            "PersonaType (UserProfile.Persona)", typeof(PersonaType),
            ["Student", "Graduate", "YoungProfessional", "Entrepreneur", "Researcher"]
        },
        {
            "ContentType (ContentItem.Type)", typeof(ContentType),
            ["Article", "Podcast", "Video", "ResearchPaper", "Book", "Documentation", "Framework", "PolicyPaper", "Essay"]
        },
        {
            "ContentLayer (ContentItem.Layer)", typeof(ContentLayer),
            [
                "Policy", "Academic", "Ideas", "Learning", "Video", "Skills",
                "RealEstate", "Law", "Literature", "Transportation", "Gaming", "Art", "History",
                "Sports", "Music", "Film", "Science", "Environment", "Travel", "Medicine",
                "Fashion", "Lifestyle", "Faith", "Philosophy", "Education",
                "Finance", "Energy", "Agriculture", "Industry", "Career",
            ]
        },
        {
            "OpportunityType (Opportunity.Type)", typeof(OpportunityType),
            [
                "Job", "Internship", "Scholarship", "GraduateProgramme", "ResearchGrant",
                "Competition", "StartupAccelerator", "Fellowship", "RemoteJob", "Event",
                "Conference", "Workshop", "Training", "Bootcamp", "Funding", "Seminar", "JobFair",
                // Appended 2026-11: the two families that nothing could reach, plus three
                // taxonomy categories that were being filed as something else.
                "Volunteering", "Tender", "Mentorship", "Award", "CallForPapers",
            ]
        },
        {
            "OpportunityTrackingStatus (saved/applied state)", typeof(OpportunityTrackingStatus),
            ["None", "Saved", "Interested", "Applied", "NotRelevant", "Accepted", "Rejected"]
        },
        {
            "InterestCategory", typeof(InterestCategory),
            ["Technology", "Business", "ResearchAndAcademia", "ProfessionalFields", "CreativeFields"]
        },
        {
            "NavigatorCardType", typeof(NavigatorCardType),
            ["Learn", "Read", "Watch", "Listen", "Apply", "Build"]
        },
        {
            "Seniority (UserProfile.Seniority)", typeof(Seniority),
            ["NotStated", "Entry", "Mid", "Senior", "Manager", "Director"]
        },
        {
            "QualificationLevel (Eligibility.MinimumQualification)", typeof(QualificationLevel),
            ["InProgress", "SecondarySchool", "Nce", "Ond", "Hnd", "Bachelors", "Masters", "Doctorate"]
        },
        {
            "SourceAccess (how a source is reached)", typeof(SourceAccess),
            ["Unknown", "FreeApi", "Feed", "PublicPortal", "Negotiated"]
        },
        {
            "Redistribution (what Radar may publish)", typeof(Redistribution),
            ["Unconfirmed", "Permitted", "LinkOnly", "DoNotPublish"]
        },
        {
            "OpportunityFamily (derived, but sent to the SPA)", typeof(OpportunityFamily),
            [
                "EmploymentAndIncome", "EducationAndDevelopment", "FundingAndEnterprise",
                "EventsAndNetworking", "ResearchAndKnowledge", "LeadershipAndRecognition",
                "SocialImpactAndParticipation", "BusinessAndInstitutional",
            ]
        },
    };

    /// <summary>
    /// Zero means "nobody said", for the two enums where silence is a legal position.
    /// </summary>
    /// <remarks>
    /// Pinned separately from the ordering above because this is a different kind of mistake.
    /// SourceAccess once had FreeApi - the most permissive of its members - at zero, so a row
    /// where nobody had recorded how the source was reached claimed a documented free API.
    /// An absent or uninitialised field has to read as ignorance, never as permission.
    /// </remarks>
    [Fact]
    public void SilenceIsTheDefaultWhereRightsAreConcerned()
    {
        Assert.Equal(SourceAccess.Unknown, default(SourceAccess));
        Assert.Equal(Redistribution.Unconfirmed, default(Redistribution));
    }
}
