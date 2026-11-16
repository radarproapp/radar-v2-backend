namespace RadarV2.Models;

/// <summary>
/// The three things Radar asks, and why each one is asked.
/// </summary>
/// <remarks>
/// Three questions, not thirty. Onboarding that asks for dozens of preferences is onboarding
/// people abandon, so the brief is explicit: ask three, then learn from what they do.
///
/// The three are not arbitrary. Each answers a different question about a person, and the feed
/// needs all three to be any good: what they care about decides what is <em>relevant</em>, what
/// they can do decides what they are <em>eligible</em> for, and where they are heading decides
/// what is <em>useful</em>. Two of the three is not enough - interests alone is how somebody
/// interested in AI gets shown every AI job including the ones requiring fifteen years.
/// </remarks>
public enum OnboardingDimension
{
    /// <summary>What the person cares about. Drives relevance.</summary>
    Interests,

    /// <summary>What they can do, and at what level. Drives eligibility.</summary>
    Capabilities,

    /// <summary>Who they are now and what they are trying to become. Drives usefulness.</summary>
    Aspirations,
}

/// <summary>
/// One set of choices inside a question.
/// </summary>
/// <remarks>
/// A question is not always one list. Graduates are asked for a qualification <em>and</em> skills
/// in the same breath, and professionals for a level and skills - one question to the person,
/// two different kinds of answer underneath, with different limits. Modelling that as two
/// questions would break the promise that there are three; modelling it as one flat list would
/// lose the difference between "pick your level" and "pick up to five skills".
/// </remarks>
/// <param name="Id">Stable key the answer is stored under. Never shown.</param>
/// <param name="Label">Heading above this set, where a question has more than one.</param>
/// <param name="MostSelections">How many may be chosen. One means it is a single choice.</param>
/// <param name="Options">The choices, in the order they are offered.</param>
public sealed record OnboardingField(
    string Id,
    string? Label,
    int MostSelections,
    IReadOnlyList<string> Options);

/// <summary>One of the three questions.</summary>
/// <param name="Id">Stable key, so an answer can be matched to what was asked.</param>
/// <param name="Dimension">Which of the three things this establishes.</param>
/// <param name="Prompt">The question as the person reads it.</param>
/// <param name="Help">The line under it, usually the selection limit.</param>
/// <param name="Fields">One set of choices, or two where the question has two parts.</param>
public sealed record OnboardingQuestion(
    string Id,
    OnboardingDimension Dimension,
    string Prompt,
    string Help,
    IReadOnlyList<OnboardingField> Fields);

/// <summary>
/// What each category is asked.
/// </summary>
/// <remarks>
/// Defined on the server rather than in the SPA, so that both ends agree on what was asked. A
/// client holding its own copy of the options is a client that can drift from the server deciding
/// what the answers mean - and the answer text is what the matching runs on, so a drift there is
/// silent and total.
///
/// The option text is the stored value. That makes rewording an option a data change rather than
/// a copy edit: every profile that chose the old wording keeps a string nothing offers any more.
/// <c>OnboardingQuestionTests</c> pins all of it for that reason, the same way the enum values
/// are pinned - a reword should be something somebody decides, not something that happens.
/// </remarks>
public static class OnboardingQuestions
{
    /// <summary>
    /// The three questions for a category.
    /// </summary>
    /// <remarks>
    /// Five personas, three question sets. Student and Graduate map to their own; everyone else
    /// gets the professional set, which is the one whose own options name them - it offers
    /// "Entrepreneur / Founder" as a level and "Research &amp; Intelligence" as a skill, so an
    /// entrepreneur and a researcher can both describe themselves accurately inside it.
    ///
    /// Worth revisiting rather than settled. If either turns out to need its own three, the
    /// mapping is this one method.
    /// </remarks>
    public static IReadOnlyList<OnboardingQuestion> For(PersonaType persona) => persona switch
    {
        PersonaType.Student => Student,
        PersonaType.Graduate => Graduate,
        _ => Professional,
    };

    /// <summary>Every set, for the tests that check all of them hold together.</summary>
    public static IReadOnlyList<IReadOnlyList<OnboardingQuestion>> All => [Student, Graduate, Professional];

    /// <summary>How many a person is asked. Three, and the brief is emphatic about it.</summary>
    public const int HowMany = 3;

    /// <summary>The usual ceiling on a multiple choice.</summary>
    public const int MostChoices = 5;

    // ── students ─────────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<OnboardingQuestion> Student =
    [
        new("student-interests", OnboardingDimension.Interests,
            "What topics are you most interested in?",
            "Select up to 5.",
            [
                new("interests", null, MostChoices,
                [
                    "Technology & AI",
                    "Business & Entrepreneurship",
                    "Finance & Investment",
                    "Leadership & Personal Development",
                    "Education & Learning",
                    "Science & Research",
                    "Health & Medicine",
                    "Law & Governance",
                    "Media & Communication",
                    "Engineering",
                    "Agriculture",
                    "Social Impact & Development",
                    "Arts & Creativity",
                    "Politics & International Affairs",
                    "Other",
                ]),
            ]),

        new("student-skills", OnboardingDimension.Capabilities,
            "What skills do you currently have or are developing?",
            "Select up to 5.",
            [
                new("skills", null, MostChoices,
                [
                    "Writing & Communication",
                    "Data Analysis",
                    "Programming & Software Development",
                    "Research",
                    "Marketing & Sales",
                    "Graphic Design & Video Editing",
                    "Business Management",
                    "Public Speaking",
                    "Project Management",
                    "Financial Analysis",
                    "Leadership",
                    "AI & Digital Tools",

                    // An answer, not a blank. Somebody who says they are still exploring has told
                    // Radar something real, and the feed owes them beginner-level material rather
                    // than the empty state it shows a person who answered nothing.
                    "None yet — I'm exploring",
                ]),
            ]),

        new("student-goals", OnboardingDimension.Aspirations,
            "What opportunities are you looking for right now?",
            "Select up to 5.",
            [
                new("goals", null, MostChoices,
                [
                    "Scholarships & Tuition Funding",
                    "Internships",
                    "Fellowships",
                    "Graduate Trainee Programmes",
                    "Competitions & Awards",
                    "Conferences & Events",
                    "Research Grants & Research Opportunities",
                    "SIWES / Industrial Training",
                    "Exchange Programmes",
                    "Leadership Programmes",
                    "Volunteering",
                    "NYSC-related opportunities",

                    // Also an answer. It means show me Information and go easy on Opportunities,
                    // which is a setting, not an absence of one.
                    "I just want to learn and stay informed",
                ]),
            ]),
    ];

    // ── graduates ────────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<OnboardingQuestion> Graduate =
    [
        new("graduate-interests", OnboardingDimension.Interests,
            "Which fields do you want to grow in?",
            "Select up to 5.",
            [
                new("interests", null, MostChoices,
                [
                    "Technology & AI",
                    "Business & Entrepreneurship",
                    "Finance & Investment",
                    "Consulting & Strategy",
                    "Data & Analytics",
                    "Marketing & Communication",
                    "Human Resources",
                    "Research & Academia",
                    "Public Policy & Governance",
                    "International Development",
                    "Engineering",
                    "Healthcare",
                    "Law",
                    "Leadership & Management",
                    "Other",
                ]),
            ]),

        new("graduate-qualifications", OnboardingDimension.Capabilities,
            "What qualifications and skills do you currently have?",
            "Select your qualification and up to 5 skills.",
            [
                // One choice, because a qualification is a floor to compare against rather than a
                // list to match. "A bachelor's degree or higher" is a comparison, which is the
                // whole reason QualificationLevel is ordered.
                //
                // Professional Certification is offered here and is deliberately not a
                // QualificationLevel: it sat above Doctorate there and made the floor wrong in
                // both directions. It belongs in Eligibility.RequiredCertifications, so whatever
                // reads this answer must route it there rather than onto the ladder.
                new("qualification", "Qualification", 1,
                [
                    "HND",
                    "Bachelor's Degree",
                    "Master's Degree",
                    "PhD",
                    "Professional Certification",
                    "Other",
                ]),

                new("skills", "Skills", MostChoices,
                [
                    "Writing & Communication",
                    "Data Analysis",
                    "Research",
                    "Programming",
                    "Marketing & Sales",
                    "Project Management",
                    "Finance",
                    "Design & Creative Skills",
                    "Business Development",
                    "AI & Digital Tools",
                    "Leadership",
                    "Other",
                ]),
            ]),

        new("graduate-goals", OnboardingDimension.Aspirations,
            "What opportunities would you like Radar to help you discover?",
            "Select up to 5.",
            [
                new("goals", null, MostChoices,
                [
                    "Entry-Level Jobs",
                    "Graduate Trainee Programmes",
                    "Internships",
                    "Remote Jobs",
                    "Fellowships",
                    "Scholarships for Postgraduate Studies",
                    "Professional Certifications",
                    "Entrepreneurship & Startup Funding",
                    "Research Opportunities",
                    "Conferences & Networking",
                    "Leadership Programmes",
                    "Career Advancement",
                    "NYSC Opportunities",
                ]),
            ]),
    ];

    // ── professionals ────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<OnboardingQuestion> Professional =
    [
        new("professional-interests", OnboardingDimension.Interests,
            "What areas do you want to stay informed and advance in?",
            "Select up to 5.",
            [
                new("interests", null, MostChoices,
                [
                    "Artificial Intelligence & Emerging Technologies",
                    "Business Strategy & Management",
                    "Entrepreneurship & Investment",
                    "Finance & Economics",
                    "Leadership & Executive Development",
                    "Industry Trends & Innovation",
                    "Research & Analytics",
                    "International Development & Policy",
                    "Marketing & Business Growth",
                    "Career Advancement",
                    "Productivity & Personal Development",
                    "Other",
                ]),
            ]),

        new("professional-capabilities", OnboardingDimension.Capabilities,
            "What is your professional background and expertise?",
            "Select your level and up to 5 skills.",
            [
                // Seniority, which is what stops an AI-interested director being shown a graduate
                // internship and an AI-interested graduate being shown a head-of-department role.
                // Both match "AI"; only one of them should see each.
                new("level", "Professional level", 1,
                [
                    "Entry-level",
                    "Mid-level",
                    "Senior-level",
                    "Manager",
                    "Director / Executive",
                    "Entrepreneur / Founder",
                ]),

                new("skills", "Skills", MostChoices,
                [
                    "Strategy & Consulting",
                    "Data Analysis & Business Intelligence",
                    "AI & Technology",
                    "Project & Programme Management",
                    "Finance & Investment",
                    "Research & Intelligence",
                    "Marketing & Growth",
                    "Leadership & People Management",
                    "Product Management",
                    "Communication & Content",
                    "Operations & Business Development",
                    "Other",
                ]),
            ]),

        new("professional-goals", OnboardingDimension.Aspirations,
            "What opportunities are you currently interested in?",
            "Select up to 5.",
            [
                new("goals", null, MostChoices,
                [
                    "Higher-Paying Jobs",
                    "Remote / International Jobs",
                    "Leadership & Executive Roles",
                    "Consulting Opportunities",
                    "Business & Investment Opportunities",
                    "Grants & Funding",
                    "Professional Certifications",
                    "Executive Education",
                    "Fellowships",
                    "Conferences & Networking",
                    "Speaking Opportunities",
                    "Board Memberships",
                    "Partnerships & Collaborations",
                    "Career Transition",
                    "Other",
                ]),
            ]),
    ];
}
