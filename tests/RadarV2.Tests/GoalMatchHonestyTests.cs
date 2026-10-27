using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A card may not claim a match it does not have.
///
/// The three items in the first test are real: they are what a Computer Science student was shown
/// under "What matters to you today", each captioned "Matches your goal: Computer Science". The
/// single word "science" scored half of "Computer Science", any score above zero recorded a
/// match, and the explanation then named the whole phrase.
/// </summary>
public class GoalMatchHonestyTests
{
    private static UserProfile Student() => new()
    {
        Id = "u1",
        Persona = PersonaType.Student,
        PrimaryGoal = "Computer Science",
        Interests = ["Computer Science"],
    };

    private static ContentItem Item(string title, string topic = "", string signal = "") => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = title,
        Topic = topic,
        Signal = signal,
        Source = "Example",
    };

    [Theory]
    [InlineData("Women's body temperature changes may increase autoimmune disease risk", "Science")]
    [InlineData("What Type of Mattress Is Right for You? (2026)", "Ideas")]
    [InlineData("Unlocking the secrets of the Dragon Man skull", "Science")]
    public void DoesNotClaimTheGoalOnSomethingThatMerelySaysScience(string title, string topic)
    {
        var explanation = InterestPersonalization.Explain(Student(), Item(title, topic));

        Assert.DoesNotContain(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StillMatchesWhenTheWholePhraseIsThere()
    {
        var item = Item("A computer science degree is not what employers test for", "Tech");

        var explanation = InterestPersonalization.Explain(Student(), item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StillMatchesWhenBothWordsAppearApart()
    {
        var item = Item(
            "Science that computer engineers keep rediscovering",
            "Tech",
            "A computer scientist explains the overlap.");

        var explanation = InterestPersonalization.Explain(Student(), item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    // A single-word goal has nothing to be a fraction of, so it is unaffected by the floor.
    [Fact]
    public void ASingleWordGoalStillMatchesOnThatWord()
    {
        var profile = new UserProfile { Id = "u2", PrimaryGoal = "Cybersecurity", Interests = ["Cybersecurity"] };
        var item = Item("Cybersecurity budgets are being cut", "Tech");

        var explanation = InterestPersonalization.Explain(profile, item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    // Two of three is enough for a longer phrase: an article on AI ethics is genuinely about
    // "Artificial Intelligence Ethics" even without all three words.
    [Fact]
    public void ALongerPhraseMatchesOnTwoThirds()
    {
        var profile = new UserProfile
        {
            Id = "u3",
            PrimaryGoal = "Artificial Intelligence Ethics",
            Interests = ["Artificial Intelligence Ethics"],
        };
        var item = Item("The ethics of artificial decision making", "Tech");

        var explanation = InterestPersonalization.Explain(profile, item);

        Assert.Contains(
            explanation.MatchedSignals,
            signal => signal.StartsWith("Goal:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The sentence had two fallbacks and so could never be wrong: with no Goal match it printed
    /// PrimaryGoal, and with no Interest match it printed the first interest. Every item on a
    /// personalised feed therefore read "it sits where computer science meets your goal" —
    /// including, measured on staging, a podcast about Esperanto and a music-industry hire.
    /// </summary>
    [Theory]
    [InlineData("Doktoro Esperanto", "Languages")]
    [InlineData("Believe hires Amazon Music's Mel Curley as Global Head", "Music")]
    [InlineData("Jane Eyre's Watercolors and the Victorian Novel", "Literature")]
    public void SaysNothingWhenNothingMatched(string title, string topic)
    {
        var why = InterestPersonalization.Explain(Student(), Item(title, topic)).Why;

        Assert.True(string.IsNullOrEmpty(why), $"Claimed a reason for an unmatched item: {why}");
    }

    // The interest fired and the goal did not. Naming the goal anyway is the same fabrication in
    // the other slot.
    [Fact]
    public void NamesOnlyTheInterestWhenTheGoalDidNotFire()
    {
        var profile = new UserProfile
        {
            Id = "u1",
            Persona = PersonaType.Student,
            PrimaryGoal = "Find a job",
            Goals = ["Find a job"],
            Interests = ["Computer Science"],
        };

        var why = InterestPersonalization.Explain(profile, Item("Computer Science curriculum rewritten", "Education")).Why;

        Assert.Contains("computer science", why, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("your goal", why, StringComparison.OrdinalIgnoreCase);
    }

    // And the matched case still reads as it did.
    [Fact]
    public void StillExplainsWhenSomethingDidMatch()
    {
        var why = InterestPersonalization.Explain(Student(), Item("Computer Science enrolment climbs", "Computer Science")).Why;

        Assert.Contains("Computer Science", why, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("This matters to you because", why);
    }

    /// <summary>
    /// An interest is not a goal, even when the ranking finds it useful to treat it as one.
    ///
    /// The goal dimension used to be seeded with the intent domain, which is derived from the
    /// interest list. A learner whose goal was "find a job" and whose interest was Computer
    /// Science was told the item spoke directly to their goal of Computer Science — a sentence
    /// they could read and know to be wrong about something they had typed themselves.
    /// </summary>
    [Fact]
    public void DoesNotNameAnInterestAsAGoal()
    {
        var profile = new UserProfile
        {
            Id = "u1",
            Persona = PersonaType.Student,
            PrimaryGoal = "Find a job",
            Goals = ["Find a job", "Learn a new skill"],
            Interests = ["Computer Science"],
        };

        var explanation = InterestPersonalization.Explain(profile, Item("Computer Science enrolment climbs", "Computer Science"));

        Assert.DoesNotContain("goal: Computer Science", explanation.Why, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Goal: Computer Science", string.Join(" ", explanation.MatchedSignals), StringComparison.OrdinalIgnoreCase);
    }
}
