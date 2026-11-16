using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// What a person picked, becoming what Radar matches on.
/// </summary>
/// <remarks>
/// The three questions were served for a while with no way to answer them: no transport for the
/// capabilities question at all, and nowhere for the two single choices to land. This is the
/// translation that closes it, and it is a translation rather than a copy - three answers are
/// real and must never be searched for, two are ladder positions that must become typed values,
/// and one is not a seniority at all.
/// </remarks>
public class OnboardingAnswerTests
{
    private static Dictionary<string, List<string>> Answers(params (string Field, string[] Picked)[] given) =>
        given.ToDictionary(g => g.Field, g => g.Picked.ToList(), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void TheThreeMultipleChoicesLandOnTheirOwnFields()
    {
        var profile = new UserProfile();

        OnboardingAnswers.Read(profile, Answers(
            ("interests", ["Technology & AI", "Finance & Investment"]),
            ("skills", ["Data Analysis", "Research"]),
            ("goals", ["Internships", "Fellowships"])));

        Assert.Equal(["Technology & AI", "Finance & Investment"], profile.Interests);
        Assert.Equal(["Data Analysis", "Research"], profile.Capabilities);
        Assert.Equal(["Internships", "Fellowships"], profile.Goals);
    }

    /// <summary>
    /// A qualification becomes a rung, so a floor can be compared against it.
    /// </summary>
    /// <remarks>
    /// The whole reason the answer is not stored as the string it arrived as. "A bachelor's
    /// degree or higher" is a comparison, and a comparison needs an ordered value.
    /// </remarks>
    [Theory]
    [InlineData("HND", QualificationLevel.Hnd)]
    [InlineData("Bachelor's Degree", QualificationLevel.Bachelors)]
    [InlineData("Master's Degree", QualificationLevel.Masters)]
    [InlineData("PhD", QualificationLevel.Doctorate)]
    public void AStatedQualificationBecomesARungOnTheLadder(string answer, QualificationLevel expected)
    {
        var profile = new UserProfile();

        OnboardingAnswers.Read(profile, Answers(("qualification", [answer])));

        Assert.Equal(expected, profile.Qualification);
    }

    /// <summary>
    /// A certification is recorded, and not as a qualification.
    /// </summary>
    /// <remarks>
    /// Offered on the same question as the degrees and belonging nowhere near them. As a rung it
    /// sat above Doctorate, so a PhD failed a "PMP required" floor and a CompTIA A+ cleared a
    /// degree one - wrong in both directions at once.
    /// </remarks>
    [Fact]
    public void ACertificationIsRecordedWithoutBecomingAQualification()
    {
        var profile = new UserProfile();

        OnboardingAnswers.Read(profile, Answers(("qualification", ["Professional Certification"])));

        Assert.Null(profile.Qualification);
        Assert.Contains("Professional Certification", profile.Certifications);
    }

    /// <summary>An answer that is not on the ladder leaves the ladder unstated.</summary>
    /// <remarks>
    /// "Other" means somebody's qualification is not in the list, which is not the same as
    /// knowing where they sit. Guessing a rung is worse than leaving it null, because null is
    /// read as "not stated" and a rung is read as a fact.
    /// </remarks>
    [Fact]
    public void AQualificationOfOtherLeavesTheLadderUnstated()
    {
        var profile = new UserProfile();

        OnboardingAnswers.Read(profile, Answers(("qualification", ["Other"])));

        Assert.Null(profile.Qualification);
        Assert.Empty(profile.Certifications);
    }

    [Theory]
    [InlineData("Entry-level", Seniority.Entry)]
    [InlineData("Mid-level", Seniority.Mid)]
    [InlineData("Senior-level", Seniority.Senior)]
    [InlineData("Manager", Seniority.Manager)]
    [InlineData("Director / Executive", Seniority.Director)]
    public void AStatedLevelBecomesASeniority(string answer, Seniority expected)
    {
        var profile = new UserProfile();

        OnboardingAnswers.Read(profile, Answers(("level", [answer])));

        Assert.Equal(expected, profile.SeniorityLevel);
    }

    /// <summary>
    /// Founder is recorded as itself, and does not become a seniority.
    /// </summary>
    /// <remarks>
    /// Offered beside the five levels and not one of them: not more senior than a director, not
    /// less than a manager, a different axis entirely. Seniority stays null, which is accurate -
    /// the question asked and this person answered something else.
    /// </remarks>
    [Fact]
    public void FounderSetsItsOwnFlagAndLeavesSeniorityUnstated()
    {
        var profile = new UserProfile();

        OnboardingAnswers.Read(profile, Answers(("level", ["Entrepreneur / Founder"])));

        Assert.True(profile.IsFounder);
        Assert.Null(profile.SeniorityLevel);
    }

    /// <summary>
    /// The answers that are real and are not search terms.
    /// </summary>
    /// <remarks>
    /// Each would poison the matching. "Other" hits "and other" in a large share of headlines and
    /// widens the feed back toward everything. "None yet - I'm exploring" splits into words and
    /// yields "im", which prefix-matches important, impact, implementation and immigration - the
    /// same shape as the "don't" to "Donald" bug this codebase was burned by once already.
    /// </remarks>
    [Theory]
    [InlineData("Other")]
    [InlineData("None yet — I'm exploring")]
    [InlineData("I just want to learn and stay informed")]
    public void AnAnswerThatIsNotASubjectNeverReachesTheMatchingTerms(string answer)
    {
        Assert.False(OnboardingAnswers.IsMatchable(answer));

        var profile = new UserProfile();
        OnboardingAnswers.Read(profile, Answers(
            ("interests", ["Technology & AI", answer]),
            ("skills", [answer]),
            ("goals", [answer])));

        Assert.Equal(["Technology & AI"], profile.Interests);
        Assert.Empty(profile.Capabilities);
        Assert.Empty(profile.Goals);
    }

    [Fact]
    public void ARealSubjectIsMatchable()
    {
        Assert.True(OnboardingAnswers.IsMatchable("Technology & AI"));
        Assert.True(OnboardingAnswers.IsMatchable("Data Analysis"));
    }

    /// <summary>
    /// A question that was not answered leaves its field alone.
    /// </summary>
    /// <remarks>
    /// Not answering is not the same as answering with nothing. A client that sends two of the
    /// three questions - resuming a half-finished onboarding, or editing one answer later - must
    /// not wipe the one it did not send.
    /// </remarks>
    [Fact]
    public void AnAbsentQuestionDoesNotWipeWhatWasAlreadyStored()
    {
        var profile = new UserProfile
        {
            Interests = ["Engineering"],
            Capabilities = ["Research"],
            Goals = ["Internships"],
            Qualification = QualificationLevel.Masters,
            SeniorityLevel = Seniority.Senior,
        };

        OnboardingAnswers.Read(profile, Answers(("interests", ["Law"])));

        Assert.Equal(["Law"], profile.Interests);
        Assert.Equal(["Research"], profile.Capabilities);
        Assert.Equal(["Internships"], profile.Goals);
        Assert.Equal(QualificationLevel.Masters, profile.Qualification);
        Assert.Equal(Seniority.Senior, profile.SeniorityLevel);
    }

    /// <summary>Answering a question with nothing does clear it.</summary>
    /// <remarks>
    /// The other half of the rule above. Sending an empty list for a question is a deliberate
    /// "none of these", which is different from not sending the question at all.
    /// </remarks>
    [Fact]
    public void AnEmptyAnswerClearsTheField()
    {
        var profile = new UserProfile { Interests = ["Engineering"] };

        OnboardingAnswers.Read(profile, Answers(("interests", [])));

        Assert.Empty(profile.Interests);
    }

    /// <summary>
    /// Every option the questions offer is either matchable or deliberately not.
    /// </summary>
    /// <remarks>
    /// The guard against the two files drifting. A new option added to a question is a new string
    /// in somebody's profile and a new term the feed is selected by, so a new opt-out answer that
    /// nobody adds to NotSubjects would start matching headlines silently.
    ///
    /// Checked by shape rather than by listing them again: anything that reads like an opt-out
    /// must be excluded, and everything else must be a real subject.
    /// </remarks>
    [Fact]
    public void EveryOfferedOptionIsEitherASubjectOrDeliberatelyExcluded()
    {
        var optOutish = new[] { "other", "none", "not sure", "just want", "n/a" };

        foreach (var option in OnboardingQuestions.All
            .SelectMany(set => set)
            .SelectMany(question => question.Fields)
            .SelectMany(field => field.Options)
            .Distinct(StringComparer.Ordinal))
        {
            var looksLikeAnOptOut = optOutish.Any(hint =>
                option.Contains(hint, StringComparison.OrdinalIgnoreCase));

            if (looksLikeAnOptOut)
            {
                Assert.False(
                    OnboardingAnswers.IsMatchable(option),
                    $"'{option}' reads as an opt-out but would be matched on as a subject.");
            }
        }
    }
}
