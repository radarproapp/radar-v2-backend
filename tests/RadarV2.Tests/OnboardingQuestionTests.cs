using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// What Radar asks, pinned.
/// </summary>
/// <remarks>
/// The option text is the stored value, so rewording an option is a data change rather than a
/// copy edit: every profile that chose the old wording keeps a string nothing offers any more,
/// and the matching runs on that string. Pinned here for the same reason the enum values are -
/// four comments saying "be careful" enforced nothing, and a test does.
/// </remarks>
public class OnboardingQuestionTests
{
    public static TheoryData<PersonaType> EveryPersona() =>
        [.. Enum.GetValues<PersonaType>()];

    /// <summary>
    /// Three questions. Every persona, including ones with no set of their own.
    /// </summary>
    /// <remarks>
    /// The brief is emphatic that onboarding is three questions, so a fourth creeping into one
    /// set is the regression worth catching. Five personas map onto three sets, so this also
    /// proves nobody falls through to nothing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPersona))]
    public void EveryPersonaIsAskedExactlyThreeQuestions(PersonaType persona)
    {
        var questions = OnboardingQuestions.For(persona);

        Assert.Equal(OnboardingQuestions.HowMany, questions.Count);
        Assert.All(questions, question =>
        {
            Assert.False(string.IsNullOrWhiteSpace(question.Id));
            Assert.False(string.IsNullOrWhiteSpace(question.Prompt));
            Assert.False(string.IsNullOrWhiteSpace(question.Help));
            Assert.NotEmpty(question.Fields);
        });
    }

    /// <summary>
    /// The three cover the three dimensions, once each.
    /// </summary>
    /// <remarks>
    /// Two of the three is not enough and the gap is not symmetrical: interests without
    /// capabilities is how somebody interested in AI is shown every AI job including the ones
    /// wanting fifteen years' experience. Relevance without eligibility reads as Radar not
    /// understanding them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPersona))]
    public void TheThreeQuestionsCoverTheThreeDimensions(PersonaType persona)
    {
        var dimensions = OnboardingQuestions.For(persona).Select(q => q.Dimension).ToList();

        Assert.Equal(
            [OnboardingDimension.Interests, OnboardingDimension.Capabilities, OnboardingDimension.Aspirations],
            dimensions);
    }

    [Theory]
    [MemberData(nameof(EveryPersona))]
    public void EveryChoiceSetOffersOptionsAndSaysHowManyMayBeChosen(PersonaType persona)
    {
        foreach (var field in OnboardingQuestions.For(persona).SelectMany(q => q.Fields))
        {
            Assert.False(string.IsNullOrWhiteSpace(field.Id));
            Assert.NotEmpty(field.Options);
            Assert.InRange(field.MostSelections, 1, OnboardingQuestions.MostChoices);

            Assert.All(field.Options, option => Assert.False(string.IsNullOrWhiteSpace(option)));

            // A duplicate option is a choice a person cannot meaningfully make, and two stored
            // answers that are the same string.
            Assert.Equal(field.Options.Count, field.Options.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>Ids are unique, since an answer is matched back to what was asked by id.</summary>
    [Fact]
    public void NoTwoQuestionsOrChoiceSetsShareAnIdWithinASet()
    {
        foreach (var set in OnboardingQuestions.All)
        {
            Assert.Equal(set.Count, set.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count());

            foreach (var question in set)
                Assert.Equal(
                    question.Fields.Count,
                    question.Fields.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>
    /// A question with two parts labels both of them.
    /// </summary>
    /// <remarks>
    /// Graduates and professionals answer two different kinds of thing under one question -
    /// a qualification or level, and skills. Unlabelled, the screen is two lists with no
    /// indication which is which.
    /// </remarks>
    [Fact]
    public void AQuestionWithMoreThanOnePartLabelsEachPart()
    {
        foreach (var question in OnboardingQuestions.All.SelectMany(set => set))
        {
            if (question.Fields.Count == 1)
            {
                Assert.Null(question.Fields[0].Label);
                continue;
            }

            Assert.All(question.Fields, field => Assert.False(string.IsNullOrWhiteSpace(field.Label)));
        }
    }

    /// <summary>
    /// A qualification and a level are one choice, not five.
    /// </summary>
    /// <remarks>
    /// Both are a position on a ladder, and the ladder is what makes "a bachelor's degree or
    /// higher" a comparison rather than a set. Five simultaneous levels is not a thing a person
    /// can be, and an eligibility floor could not be checked against one.
    /// </remarks>
    [Fact]
    public void QualificationAndLevelAreSingleChoices()
    {
        var singles = OnboardingQuestions.All
            .SelectMany(set => set)
            .SelectMany(question => question.Fields)
            .Where(field => field.Id is "qualification" or "level")
            .ToList();

        Assert.Equal(2, singles.Count);
        Assert.All(singles, field => Assert.Equal(1, field.MostSelections));
    }

    /// <summary>
    /// Saying you have none yet is an answer.
    /// </summary>
    /// <remarks>
    /// The difference between a person who has not answered and one who answered "I am still
    /// exploring" is the difference between an empty state and a beginner's feed. Radar asks
    /// rather than infers, and the reward for answering cannot be the screen shown to somebody
    /// who did not.
    /// </remarks>
    [Fact]
    public void ThereIsAWayToSayNotYetWithoutLeavingTheQuestionBlank()
    {
        var studentSkills = OnboardingQuestions.Student
            .Single(q => q.Dimension == OnboardingDimension.Capabilities)
            .Fields[0];

        var studentGoals = OnboardingQuestions.Student
            .Single(q => q.Dimension == OnboardingDimension.Aspirations)
            .Fields[0];

        Assert.Contains("None yet — I'm exploring", studentSkills.Options);
        Assert.Contains("I just want to learn and stay informed", studentGoals.Options);
    }

    /// <summary>
    /// Professional Certification is offered as a qualification and is not one of the levels.
    /// </summary>
    /// <remarks>
    /// It was briefly a QualificationLevel, sitting above Doctorate, which made the floor
    /// comparison wrong in both directions - a PhD failed "PMP required" and a CompTIA A+ cleared
    /// "bachelor's or higher". It is still a legitimate thing to tell Radar, so it stays on the
    /// question; whatever reads the answer has to route it to
    /// Eligibility.RequiredCertifications rather than onto the ladder.
    /// </remarks>
    [Fact]
    public void ACertificationIsOfferedAsAnAnswerButIsNotARungOnTheLadder()
    {
        var qualification = OnboardingQuestions.Graduate
            .Single(q => q.Dimension == OnboardingDimension.Capabilities)
            .Fields.Single(f => f.Id == "qualification");

        Assert.Contains("Professional Certification", qualification.Options);
        Assert.DoesNotContain(
            "ProfessionalCertification",
            Enum.GetNames<QualificationLevel>());
    }

    /// <summary>
    /// The exact wording, pinned.
    /// </summary>
    /// <remarks>
    /// Counts rather than every string, so the test stays readable - but a count catches the
    /// thing that actually happens, which is an option being added or quietly dropped. Changing
    /// one of these numbers should mean somebody decided to, and thought about the profiles
    /// holding the old answer.
    /// </remarks>
    [Theory]
    [InlineData("student-interests", "interests", 15)]
    [InlineData("student-skills", "skills", 13)]
    [InlineData("student-goals", "goals", 13)]
    [InlineData("graduate-interests", "interests", 15)]
    [InlineData("graduate-qualifications", "qualification", 6)]
    [InlineData("graduate-qualifications", "skills", 12)]
    [InlineData("graduate-goals", "goals", 13)]
    [InlineData("professional-interests", "interests", 12)]
    [InlineData("professional-capabilities", "level", 6)]
    [InlineData("professional-capabilities", "skills", 12)]
    [InlineData("professional-goals", "goals", 15)]
    public void TheOptionsOfferedAreTheOnesTheBriefListed(string questionId, string fieldId, int howMany)
    {
        var field = OnboardingQuestions.All
            .SelectMany(set => set)
            .Single(question => question.Id == questionId)
            .Fields.Single(f => f.Id == fieldId);

        Assert.Equal(howMany, field.Options.Count);
    }

    /// <summary>Everyone who is not a student or a graduate gets the professional set.</summary>
    /// <remarks>
    /// Its own options are what make that defensible: it offers "Entrepreneur / Founder" as a
    /// level and "Research &amp; Intelligence" as a skill, so both can describe themselves
    /// accurately inside it rather than being filed as something they are not.
    /// </remarks>
    [Theory]
    [InlineData(PersonaType.YoungProfessional)]
    [InlineData(PersonaType.Entrepreneur)]
    [InlineData(PersonaType.Researcher)]
    public void PersonasWithoutTheirOwnSetGetTheProfessionalOne(PersonaType persona)
    {
        Assert.Same(OnboardingQuestions.Professional, OnboardingQuestions.For(persona));

        var level = OnboardingQuestions.For(persona)
            .Single(q => q.Dimension == OnboardingDimension.Capabilities)
            .Fields.Single(f => f.Id == "level");

        Assert.Contains("Entrepreneur / Founder", level.Options);
    }

    [Fact]
    public void StudentsAndGraduatesGetTheirOwn()
    {
        Assert.Same(OnboardingQuestions.Student, OnboardingQuestions.For(PersonaType.Student));
        Assert.Same(OnboardingQuestions.Graduate, OnboardingQuestions.For(PersonaType.Graduate));
    }
}
