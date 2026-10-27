using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// A performance summary reports what the learner answered. It does not characterise them.
///
/// Counted from the answers stored on the quiz rather than generated, so it works with the cloud
/// brain unavailable — which it currently is, every call falling back. A summary that vanishes
/// when a key runs out is not a summary.
/// </summary>
public class QuizPerformanceTests
{
    private static MentorQuizQuestion Q(
        string question,
        int correctIndex,
        int? answeredIndex,
        string level = "Understanding",
        string? subtopic = null,
        string type = "MCQ") => new()
    {
        Question = question,
        Options = ["first", "second", "third"],
        CorrectIndex = correctIndex,
        Explanation = "Because of the thing.",
        CognitiveLevel = level,
        Subtopic = subtopic,
        QuestionType = type,
        UserAnswered = answeredIndex is not null,
        UserAnswerIndex = answeredIndex,
        IsCorrect = answeredIndex is not null ? answeredIndex == correctIndex : null,
    };

    private static MentorQuiz Quiz(params MentorQuizQuestion[] questions) => new()
    {
        Id = "q1",
        Topic = "Cybersecurity",
        Difficulty = "Intermediate",
        Questions = [.. questions],
    };

    [Fact]
    public void ScoresOnlyWhatWasAnswered()
    {
        // Three questions, two answered, one of those right.
        var summary = QuizPerformance.Build(Quiz(
            Q("a", 0, 0),
            Q("b", 1, 2),
            Q("c", 0, null)));

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.Answered);
        Assert.Equal(1, summary.Correct);
        Assert.Equal(50, summary.ScorePercent);
    }

    /// <summary>
    /// An abandoned quiz is not a bad result. Counting the unanswered questions as wrong would
    /// turn walking away into a score, which is a claim about the learner nobody made.
    /// </summary>
    [Fact]
    public void AbandoningAQuizIsNotScoredAsFailure()
    {
        var summary = QuizPerformance.Build(Quiz(
            Q("a", 0, 0),
            Q("b", 1, null),
            Q("c", 2, null)));

        Assert.Equal(100, summary.ScorePercent);
        Assert.Equal(1, summary.Answered);
    }

    // Nothing answered is not a score of zero — it is no score.
    [Fact]
    public void NothingAnsweredHasNoScore()
    {
        var summary = QuizPerformance.Build(Quiz(Q("a", 0, null), Q("b", 1, null)));

        Assert.Null(summary.ScorePercent);
        Assert.Empty(summary.Missed);
        Assert.Empty(summary.WeakAreas);
    }

    [Fact]
    public void BreaksDownByCognitiveLevel()
    {
        var summary = QuizPerformance.Build(Quiz(
            Q("a", 0, 0, level: "Recall"),
            Q("b", 0, 0, level: "Recall"),
            Q("c", 0, 1, level: "ProblemSolving")));

        var recall = summary.ByCognitiveLevel.Single(b => b.Name == "Recall");
        var solving = summary.ByCognitiveLevel.Single(b => b.Name == "ProblemSolving");

        Assert.Equal(100, recall.Percent);
        Assert.Equal(0, solving.Percent);
    }

    [Fact]
    public void AMissedQuestionCarriesBothAnswersAndTheExplanation()
    {
        var summary = QuizPerformance.Build(Quiz(Q("What is a nonce?", 2, 0)));

        var missed = Assert.Single(summary.Missed);
        Assert.Equal("first", missed.YourAnswer);
        Assert.Equal("third", missed.CorrectAnswer);
        Assert.Equal("Because of the thing.", missed.Explanation);
    }

    /// <summary>
    /// A weak area needs more wrong than right. One of five wrong is not a weakness, and naming
    /// it on that evidence is the kind of claim this codebase keeps removing.
    /// </summary>
    [Fact]
    public void OneWrongAnswerInFiveIsNotAWeakArea()
    {
        var summary = QuizPerformance.Build(Quiz(
            Q("a", 0, 0, subtopic: "TLS"),
            Q("b", 0, 0, subtopic: "TLS"),
            Q("c", 0, 0, subtopic: "TLS"),
            Q("d", 0, 0, subtopic: "TLS"),
            Q("e", 0, 1, subtopic: "TLS")));

        Assert.Empty(summary.WeakAreas);
    }

    [Fact]
    public void MostlyWrongInASubtopicIsAWeakArea()
    {
        var summary = QuizPerformance.Build(Quiz(
            Q("a", 0, 1, subtopic: "Key exchange"),
            Q("b", 0, 1, subtopic: "Key exchange"),
            Q("c", 0, 0, subtopic: "Key exchange")));

        Assert.Equal("Key exchange", Assert.Single(summary.WeakAreas));
    }

    // Questions with no declared subtopic are left out rather than bucketed under a placeholder.
    [Fact]
    public void QuestionsWithoutASubtopicAreNotInvented()
    {
        var summary = QuizPerformance.Build(Quiz(
            Q("a", 0, 1),
            Q("b", 0, 1, subtopic: "Hashing")));

        Assert.Equal("Hashing", Assert.Single(summary.BySubtopic).Name);
    }

    // Free-text answers have no option index; the typed answer is what to show back.
    [Fact]
    public void AShortAnswerShowsWhatWasTyped()
    {
        var question = Q("Explain salting", 0, null, type: "ShortAnswer");
        question.UserAnswered = true;
        question.UserTextAnswer = "adding random data before hashing";
        question.IsCorrect = false;

        var summary = QuizPerformance.Build(Quiz(question));

        Assert.Equal("adding random data before hashing", Assert.Single(summary.Missed).YourAnswer);
    }
}
