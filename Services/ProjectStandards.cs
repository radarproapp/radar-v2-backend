using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// What a project is judged against, for a stage and a kind of submission.
/// </summary>
/// <remarks>
/// Authored, like <see cref="AssessmentRubric"/> and for the same reason: a standard is a claim
/// about what good work looks like, and it belongs in the product where somebody can disagree
/// with it. Asking a model to invent the criteria <i>and</i> apply them means nobody knows what
/// the judgement was against, and the criteria drift between two runs on the same project.
///
/// Stage decides the bar, not the subject. A beginner's project is judged on whether it does what
/// it says and can be followed; an advanced one on whether the decisions behind it were made
/// deliberately and defended. Expecting the latter of a first project is how somebody is told
/// their work is inadequate for not being something nobody asked them to build.
///
/// One criterion changes with the submission rather than the stage, and it is the honest one:
/// what Radar actually had in front of it. For a document or a repository that is the work; for
/// an image or a video it is the learner's written account, because the engine reads text and
/// nothing else. Judging a description while implying the artefact was seen is the same
/// fabrication as a summary written from a headline.
/// </remarks>
public static class ProjectStandards
{
    private static AssessmentCriterion C(string name, string asks) =>
        new() { Name = name, Asks = asks };

    /// <summary>
    /// The criteria for a stage, plus the one that depends on what was handed in.
    /// </summary>
    public static List<AssessmentCriterion> For(string? stage, SubmissionKind kind) =>
        [.. ForStage(stage), EvidenceCriterion(kind)];

    private static List<AssessmentCriterion> ForStage(string? stage) =>
        (stage ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "advanced" or "application" => Advanced(),
            "intermediate" => Intermediate(),
            _ => Beginner(),
        };

    /// <summary>
    /// A first project. Judged on whether it exists, works and can be followed.
    /// </summary>
    /// <remarks>
    /// Deliberately not judged on originality, scale or polish. The point of a first project is
    /// to finish one.
    /// </remarks>
    private static List<AssessmentCriterion> Beginner() =>
    [
        C("It does what it says",
            "Does the work actually do the thing the project set out to do? Not partly, not a plan "
            + "for doing it later - the thing itself."),

        C("It is finished",
            "Is this a whole piece of work rather than a start? A project that stops halfway "
            + "teaches the habit of stopping halfway."),

        C("Someone else could follow it",
            "Could another person understand what was built and how, from what is here? A project "
            + "nobody but its author can read is not yet evidence of anything."),

        C("The basics are right",
            "Are the fundamentals of this kind of work present and correct - naming, structure, "
            + "accuracy? Not elegance; correctness."),
    ];

    private static List<AssessmentCriterion> Intermediate() =>
    [
        C("It does what it says",
            "Does the work do the thing it set out to do, in full?"),

        C("The choices are explained",
            "Where there was more than one way to do this, is it clear which was chosen and why? "
            + "An unexplained choice is indistinguishable from an accident."),

        C("It handles the awkward cases",
            "Does it cope with the input, user or situation that does not behave? Work that only "
            + "handles the easy path has not been tested against reality."),

        C("Someone else could build on it",
            "Is this structured so another person could extend it rather than restart it?"),

        C("It is presented for a reader",
            "Is there enough context for somebody encountering this cold - what it is, what "
            + "problem it addresses, how to run or read it?"),
    ];

    /// <summary>
    /// Work that is meant to be shown to an employer or a client.
    /// </summary>
    private static List<AssessmentCriterion> Advanced() =>
    [
        C("It solves a real problem",
            "Is the problem one somebody actually has, stated clearly enough that its solution "
            + "can be judged? An exercise dressed as a product is still an exercise."),

        C("The decisions are defended",
            "Are the significant choices named, with the alternatives and the reason for this "
            + "one? At this stage judgement is the thing being demonstrated."),

        C("The limits are stated",
            "Does the work say what it does not do, and where it would break? Claiming no limits "
            + "is the clearest evidence they have not been looked for."),

        C("It stands up to scrutiny",
            "Would somebody who does this professionally find the approach sound? Not perfect - "
            + "sound."),

        C("It is evidenced",
            "Are the claims about what it achieves backed by something checkable - output, "
            + "measurements, a demonstration - rather than asserted?"),

        C("It is ready to show",
            "Could this go in front of an employer or a client as it stands?"),
    ];

    /// <summary>
    /// What Radar actually had to judge.
    /// </summary>
    /// <remarks>
    /// Named as a criterion rather than buried in a caveat, so it is visible on the same list as
    /// everything else. For an image or a video the artefact was never seen: the engine reads
    /// text. Assessing the learner's account of their work is a reasonable thing to do and a
    /// dishonest thing to hide.
    /// </remarks>
    private static AssessmentCriterion EvidenceCriterion(SubmissionKind kind) => kind switch
    {
        SubmissionKind.Image or SubmissionKind.Video => C(
            "Your account of it",
            "Radar cannot see an image or watch a video, so this judges what you wrote about what "
            + "you built. Does your account say clearly what it is, what you did, and what it "
            + "achieves? This criterion is about the description, not the work itself."),

        // Radar does not fetch the link. Reaching out to an address somebody typed is a request
        // the server makes on their behalf, and doing that safely means refusing private ranges,
        // redirects into them, and anything that is not plain https - which is a piece of work in
        // its own right rather than a line in a rubric. Until then this judges the account, and
        // says so, rather than implying a page was read.
        SubmissionKind.Link => C(
            "Your account of it",
            "Radar does not open links yet, so this judges what you wrote about what is there. "
            + "Does your account say what the link leads to, what you built, and what it "
            + "achieves? This criterion is about the description, not the work itself."),

        _ => C(
            "It is legible as submitted",
            "Can the work be read as handed in - the right file, complete, and not a fragment or "
            + "an export that lost its structure?"),
    };
}
