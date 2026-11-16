namespace RadarV2.Models;

/// <summary>
/// The eight families an opportunity belongs to.
/// </summary>
/// <remarks>
/// A grouping above <see cref="OpportunityType"/>, not a replacement for it. The type is what a
/// thing <em>is</em> - a scholarship, a tender - and is persisted by value on every opportunity
/// already stored. The family is how a person thinks about what they are looking for, and is
/// what the third onboarding question asks them to choose from. Seventeen types in one menu is
/// not a question anybody answers well.
///
/// Eight rather than thirty, so the engine has room to grow without the database being
/// redesigned each time a category appears.
/// </remarks>
public enum OpportunityFamily
{
    /// <summary>Jobs, internships, graduate programmes, freelance gigs, contracts, consulting.</summary>
    EmploymentAndIncome,

    /// <summary>Scholarships, training sponsorships, certifications, bootcamps, exchanges.</summary>
    EducationAndDevelopment,

    /// <summary>Grants, startup funding, business competitions, incubators, accelerators.</summary>
    FundingAndEnterprise,

    /// <summary>Conferences, seminars, workshops, job fairs, networking, hackathons.</summary>
    EventsAndNetworking,

    /// <summary>Research grants, assistantships, calls for papers, collaborations.</summary>
    ResearchAndKnowledge,

    /// <summary>Fellowships, mentorship, awards, ambassador programmes, speaking.</summary>
    LeadershipAndRecognition,

    /// <summary>Volunteering, NGO programmes, community initiatives, policy consultations.</summary>
    SocialImpactAndParticipation,

    /// <summary>Tenders, procurement, RFPs, EOIs, supplier registrations, partnerships.</summary>
    BusinessAndInstitutional,
}

/// <summary>
/// Which family a type belongs to, and what each family is called on a screen.
/// </summary>
/// <remarks>
/// One mapping, in one place. The alternative - a switch at each call site - is how the feed card
/// and the brief it links to ended up disagreeing about the same item once before.
/// </remarks>
public static class OpportunityFamilies
{
    /// <summary>
    /// The family a stored type belongs to.
    /// </summary>
    /// <remarks>
    /// Total by construction: a new <see cref="OpportunityType"/> that nobody files here lands in
    /// Employment and Income, which is wrong but visible, rather than throwing on a feed render.
    /// The test that enumerates every type is what actually stops one being forgotten.
    /// </remarks>
    public static OpportunityFamily Of(OpportunityType type) => type switch
    {
        OpportunityType.Job or OpportunityType.Internship or OpportunityType.RemoteJob
            or OpportunityType.GraduateProgramme
            => OpportunityFamily.EmploymentAndIncome,

        OpportunityType.Scholarship or OpportunityType.Training or OpportunityType.Bootcamp
            => OpportunityFamily.EducationAndDevelopment,

        OpportunityType.Funding or OpportunityType.StartupAccelerator or OpportunityType.Competition
            => OpportunityFamily.FundingAndEnterprise,

        OpportunityType.Event or OpportunityType.Conference or OpportunityType.Workshop
            or OpportunityType.Seminar or OpportunityType.JobFair
            => OpportunityFamily.EventsAndNetworking,

        OpportunityType.ResearchGrant
            => OpportunityFamily.ResearchAndKnowledge,

        OpportunityType.Fellowship
            => OpportunityFamily.LeadershipAndRecognition,

        _ => OpportunityFamily.EmploymentAndIncome,
    };

    /// <summary>The family in the words the third onboarding question uses.</summary>
    public static string Label(OpportunityFamily family) => family switch
    {
        OpportunityFamily.EmploymentAndIncome => "Employment & Income",
        OpportunityFamily.EducationAndDevelopment => "Education & Development",
        OpportunityFamily.FundingAndEnterprise => "Funding & Enterprise",
        OpportunityFamily.EventsAndNetworking => "Events & Networking",
        OpportunityFamily.ResearchAndKnowledge => "Research & Knowledge",
        OpportunityFamily.LeadershipAndRecognition => "Leadership & Recognition",
        OpportunityFamily.SocialImpactAndParticipation => "Social Impact & Participation",
        OpportunityFamily.BusinessAndInstitutional => "Business & Institutional",
        _ => family.ToString(),
    };

    /// <summary>What the family covers, for the line under the option.</summary>
    /// <remarks>
    /// Somebody choosing "Business & Institutional" has to be able to tell it means tenders and
    /// procurement rather than business news, without guessing.
    /// </remarks>
    public static string Covers(OpportunityFamily family) => family switch
    {
        OpportunityFamily.EmploymentAndIncome => "Jobs, internships, graduate programmes, freelance and contract work",
        OpportunityFamily.EducationAndDevelopment => "Scholarships, training sponsorships, certifications, bootcamps, exchanges",
        OpportunityFamily.FundingAndEnterprise => "Grants, startup funding, business competitions, incubators, accelerators",
        OpportunityFamily.EventsAndNetworking => "Conferences, seminars, workshops, job fairs, networking, hackathons",
        OpportunityFamily.ResearchAndKnowledge => "Research grants, assistantships, calls for papers, collaborations",
        OpportunityFamily.LeadershipAndRecognition => "Fellowships, mentorship, awards, ambassador programmes, speaking",
        OpportunityFamily.SocialImpactAndParticipation => "Volunteering, NGO programmes, community initiatives, policy consultations",
        OpportunityFamily.BusinessAndInstitutional => "Tenders, procurement, RFPs, supplier registration, partnerships",
        _ => string.Empty,
    };

    /// <summary>Every family, in the order the question lists them.</summary>
    public static IReadOnlyList<OpportunityFamily> All { get; } = [.. Enum.GetValues<OpportunityFamily>()];
}
