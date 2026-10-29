using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// What each kind of document is judged against, for each kind of learner.
/// </summary>
/// <remarks>
/// Authored rather than generated, deliberately. A rubric is a statement about what good work
/// looks like — it belongs in the product, reviewed, where somebody can disagree with it. Asking
/// a model to invent the criteria *and* apply them means nobody knows what the judgement was
/// against, and the criteria would drift between two runs on the same essay.
///
/// This is the same distinction that kept the project templates and removed the paper catalogue:
/// describing a kind of work is authorship, asserting a fact about the world is fabrication.
///
/// Persona changes the criteria rather than the tone. A graduate's CV is read for whether it
/// evidences a role they have not held yet; a working professional's is read for progression and
/// scope. Telling both the same things is how generic feedback happens.
/// </remarks>
public static class AssessmentRubric
{
    /// <summary>
    /// A post, caption, thread or content calendar written to be published.
    /// </summary>
    /// <remarks>
    /// Judged as writing with a job to do, not as an essay that happens to be short. The usual
    /// failure in content writing is not grammar — it is a post that reads well and asks nobody to
    /// do anything, or one written for the brand rather than the person scrolling past it.
    ///
    /// Engagement numbers are deliberately absent. Radar cannot see how a post performed, and a
    /// criterion it cannot assess from the document in front of it would be a guess wearing a
    /// rubric's clothes.
    /// </remarks>
    private static List<AssessmentCriterion> SocialMediaContent(PersonaType persona) =>
    [
        C("Hook",
            "Does the opening line earn the second one? A reader decides in about a second, and a "
            + "post that starts by introducing itself has already lost them."),

        C("One idea",
            "Is there a single thing this post is about? Content that makes three points makes "
            + "none, because a reader scrolling takes away at most one."),

        C("Written for a reader",
            "Is this addressed to a person with a problem, or to the organisation that published "
            + "it? Announcements about a brand are written from the inside out."),

        C("Voice and platform fit",
            "Does the length, tone and shape suit where it is going? The same words that work in "
            + "a LinkedIn post do not work as a caption, and nothing about the document should "
            + "have to be explained by the platform it is for."),

        C("Call to action",
            "Is the reader asked to do one specific thing, and is it the thing this post actually "
            + "set up? A post that ends with 'thoughts?' is asking for engagement, not an action."),

        C("Claims it can stand behind",
            "Are statistics, outcomes and quotes attributable? Content is where unsourced numbers "
            + "travel furthest, and the person who posts one owns it."),

        persona == PersonaType.Student
            ? C("Evidence of your own thinking",
                "Does this show what you understand, rather than restating what the source said? "
                + "Content built only from other people's posts demonstrates reading, not judgement.")
            : C("Business outcome",
                "Is it clear what this post is for — demand, hiring, credibility, launch? Content "
                + "without a purpose behind it cannot be judged as having worked."),
    ];

    /// <summary>
    /// An application for money: a grant, a fellowship, a scholarship.
    /// </summary>
    /// <remarks>
    /// The one Radar's readers need most. Scholarships and grants are what the feed surfaces more
    /// than anything else, and the application is the thing standing between somebody and one.
    ///
    /// Judged as a case made to a stranger with a budget and a pile of other applications, not as
    /// an essay about a plan. The common failure is not bad writing; it is answering a question
    /// the funder did not ask.
    /// </remarks>
    private static List<AssessmentCriterion> GrantApplication(PersonaType persona) =>
    [
        C("It answers what was asked",
            "Does it respond to the funder's actual criteria, in their terms? An application that "
            + "describes a worthy project without addressing what the fund exists to do is read "
            + "once and set aside."),

        C("The need is evidenced",
            "Is the problem shown with something checkable - figures, sources, lived specifics - "
            + "rather than asserted as self-evident? Every applicant believes their need is "
            + "obvious."),

        C("The plan is concrete",
            "Does it say what will actually be done, by whom, by when? A reviewer is deciding "
            + "whether this can happen, and intentions do not answer that."),

        C("The money is accounted for",
            "Is it clear what the funds buy and why that amount? An unexplained figure invites "
            + "the reviewer to invent their own, which is always lower."),

        C("The outcome is measurable",
            "Can somebody tell afterwards whether this worked? 'Raise awareness' cannot be "
            + "checked and so cannot be funded twice."),

        C("You are the right person",
            "Does it establish why this applicant, specifically, can deliver this? Not a CV - the "
            + "connection between what they have done and what they propose."),

        persona == PersonaType.Student
            ? C("It is about the work, not the hardship",
                "Does it make an academic or practical case rather than relying on circumstance? "
                + "Circumstance belongs where the form asks for it, and nowhere else.")
            : C("It fits the funder",
                "Is there evidence of having read what this funder has backed before? A generic "
                + "application is recognisable from the first paragraph."),
    ];

    /// <summary>
    /// Slides written to raise money or win a decision.
    /// </summary>
    /// <remarks>
    /// Judged as a sequence of claims, not as design. A deck that looks professional and argues
    /// nothing is the common case, and the thing a reviewer is actually deciding is whether the
    /// story holds.
    /// </remarks>
    private static List<AssessmentCriterion> PitchDeck() =>
    [
        C("The problem is real and specific",
            "Is it a problem somebody has today, named precisely enough to be recognised by "
            + "whoever has it? A market described in billions is not a problem."),

        C("The solution follows from it",
            "Does what is being built obviously address the problem just stated, or is there a "
            + "leap between the two slides?"),

        C("There is evidence of demand",
            "Is there something beyond conviction - users, revenue, a waiting list, a letter of "
            + "intent? At the earliest stage, evidence of conversations counts; assertion does "
            + "not."),

        C("The numbers are defensible",
            "Do the figures have a stated basis, and do they survive one question? A number "
            + "nobody can trace is worse than no number."),

        C("The ask is clear",
            "Does it say what is wanted, what it buys and what happens as a result? A deck that "
            + "ends without an ask has wasted the meeting."),

        C("It is sequenced",
            "Does each slide earn the next, so a reader arrives at the ask already convinced "
            + "rather than being asked to assemble the case themselves?"),
    ];

    /// <summary>
    /// A README, an API reference, a runbook: writing that exists to be used.
    /// </summary>
    /// <remarks>
    /// Judged on whether somebody can do the thing, which is the only question documentation
    /// answers. Completeness is not the measure - a short page that gets somebody working beats a
    /// thorough one that does not.
    /// </remarks>
    private static List<AssessmentCriterion> TechnicalDocumentation() =>
    [
        C("Someone can get started",
            "Can a reader who has never seen this get to a working state from what is here, "
            + "without asking anybody? That is the whole job."),

        C("It says what this is for",
            "Is it clear what the thing does and when to reach for it, before the instructions "
            + "begin?"),

        C("The steps are complete and in order",
            "Does every step have what it needs - the prerequisite, the exact command, the "
            + "expected result - in the order it is done?"),

        C("It says what goes wrong",
            "Are the common failures named with what to do about them? The gap between working "
            + "and not is where readers actually spend their time."),

        C("It is accurate now",
            "Does it match what the thing currently does? Documentation describing a previous "
            + "version is worse than none, because it is trusted."),

        C("It is written for a reader",
            "Is the assumed knowledge stated, and is the jargon either avoided or defined on "
            + "first use?"),
    ];

    /// <summary>
    /// A plan for teaching something to somebody.
    /// </summary>
    private static List<AssessmentCriterion> LessonPlan() =>
    [
        C("The learner comes out able to do something",
            "Is the intended outcome stated as something a learner can do afterwards, rather "
            + "than as a topic to be covered? 'Understand fractions' cannot be observed."),

        C("It is built for who is in the room",
            "Does it say what the learners already know, and does the plan start from there?"),

        C("The time is realistic",
            "Does the timing survive contact with a real session, including the part where "
            + "somebody asks a question?"),

        C("Learners do something",
            "Is there a point at which the learner practises rather than listens? A plan that is "
            + "entirely delivery is a lecture."),

        C("You can tell whether it worked",
            "Is there a check during or at the end that would reveal a learner who has not "
            + "followed? Without one the plan cannot be adjusted."),

        C("It anticipates the sticking point",
            "Does it name where learners usually get stuck in this material, and what to do when "
            + "they do? That knowledge is what separates a plan from a running order."),
    ];

    private static AssessmentCriterion C(string name, string asks) =>
        new() { Name = name, Asks = asks };

    /// <summary>
    /// The criteria for a document kind and a persona, in the order they should be read.
    /// </summary>
    /// <remarks>
    /// Returns an empty list for <see cref="DocumentKind.Unknown"/>: if the kind could not be
    /// identified there is nothing to judge it against, and picking a default rubric is how a CV
    /// gets told its argument is underdeveloped.
    /// </remarks>
    public static List<AssessmentCriterion> For(DocumentKind kind, PersonaType persona) => kind switch
    {
        DocumentKind.Essay => Essay(persona),
        DocumentKind.Report => Report(persona),
        DocumentKind.Cv => Cv(persona),
        DocumentKind.CoverLetter => CoverLetter(),
        DocumentKind.ResearchProposal => ResearchProposal(),
        DocumentKind.PersonalStatement => PersonalStatement(),
        DocumentKind.BusinessPlan => BusinessPlan(),
        DocumentKind.SocialMediaContent => SocialMediaContent(persona),
        DocumentKind.GrantApplication => GrantApplication(persona),
        DocumentKind.PitchDeck => PitchDeck(),
        DocumentKind.TechnicalDocumentation => TechnicalDocumentation(),
        DocumentKind.LessonPlan => LessonPlan(),
        _ => [],
    };

    private static List<AssessmentCriterion> Essay(PersonaType persona)
    {
        var criteria = new List<AssessmentCriterion>
        {
            C("A position", "Does the essay state a claim it then defends, rather than surveying a topic?"),
            C("Evidence", "Is each claim supported, and is the support cited rather than asserted?"),
            C("Analysis", "Does it explain why the evidence supports the claim, or does it stop at presenting it?"),
            C("Structure", "Does each paragraph do one job, and do they build in an order that has a reason?"),
            C("Counterargument", "Is the strongest objection addressed rather than the easiest one?"),
        };

        // A student is marked on referencing; nobody else is.
        if (persona == PersonaType.Student)
            criteria.Add(C("Referencing", "Are sources cited consistently in one recognised style, and does every claim that needs one have one?"));

        return criteria;
    }

    private static List<AssessmentCriterion> Report(PersonaType persona)
    {
        var criteria = new List<AssessmentCriterion>
        {
            C("The ask", "Can a reader tell in the first paragraph what decision this report supports?"),
            C("Findings before interpretation", "Are the findings separable from the writer's reading of them?"),
            C("Method", "Is it clear where the numbers came from and what they exclude?"),
            C("Recommendation", "Is there a recommendation specific enough to act on, with its cost or risk named?"),
        };

        if (persona is PersonaType.YoungProfessional or PersonaType.Entrepreneur)
            criteria.Add(C("Audience", "Is it written for the person who will act on it, rather than for the person who wrote it?"));

        return criteria;
    }

    private static List<AssessmentCriterion> Cv(PersonaType persona) => persona switch
    {
        // Nothing held yet, so the question is whether the evidence substitutes for experience.
        PersonaType.Student or PersonaType.Graduate =>
        [
            C("A target", "Is it clear what role this is for? A CV aimed at everything reads as aimed at nothing."),
            C("Evidence over duties", "Does each entry say what was achieved and how it is known, rather than what the role involved?"),
            C("Projects carry weight", "Where experience is thin, is project or coursework evidence specific and verifiable?"),
            C("Skills claimed are shown", "Is every skill listed demonstrated somewhere else on the page?"),
            C("Readable in thirty seconds", "Can a reader find the target role, the strongest evidence and the contact details that fast?"),
        ],

        _ =>
        [
            C("A target", "Is it clear what role this is for, and does the ordering serve that role?"),
            C("Scope and progression", "Does the history show growth in responsibility rather than a list of posts?"),
            C("Outcomes, quantified", "Are results given with a number or a comparison, rather than as adjectives?"),
            C("Recency weighting", "Does the most relevant recent work get the most space?"),
            C("Readable in thirty seconds", "Can a reader find the target role, the strongest evidence and the contact details that fast?"),
        ],
    };

    private static List<AssessmentCriterion> CoverLetter() =>
    [
        C("Addressed to this role", "Does it name why this employer and this role, specifically?"),
        C("One argument", "Does it make a single case for the writer, rather than restating the CV?"),
        C("Evidence", "Is the claim backed by something that happened, with enough detail to be checked?"),
        C("Length", "Is it short enough to be read in full?"),
    ];

    private static List<AssessmentCriterion> ResearchProposal() =>
    [
        C("A question", "Is there a question narrow enough to answer and open enough to be worth asking?"),
        C("The gap", "Does it say what is already known and what is missing, with reference to actual work?"),
        C("Method fits the question", "Would the proposed method actually answer the question asked?"),
        C("Feasibility", "Is the scope achievable with the time, access and data stated?"),
        C("Contribution", "Is it clear what changes if this succeeds?"),
    ];

    private static List<AssessmentCriterion> PersonalStatement() =>
    [
        C("A reason", "Does it give a specific reason for this course or programme, not a general enthusiasm?"),
        C("Shown, not claimed", "Is each quality demonstrated by something that happened?"),
        C("Fit", "Does it connect what the writer has done to what the programme actually offers?"),
        C("Voice", "Does it read as one person, rather than as a template?"),
    ];

    private static List<AssessmentCriterion> BusinessPlan() =>
    [
        C("The problem", "Is the problem described from the customer's side, with evidence it is felt?"),
        C("The market", "Are the market claims sourced, and is the addressable part distinguished from the total?"),
        C("Unit economics", "Is it clear what one sale costs and earns?"),
        C("Why now", "Does it say what changed to make this possible or necessary now?"),
        C("The risk", "Is the thing most likely to kill it named, rather than a list of minor risks?"),
    ];
}
