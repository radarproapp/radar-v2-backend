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
