using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// What a learner actually got right, and where.
/// </summary>
/// <remarks>
/// Computed from the answers stored on the quiz, so it needs no model call and works with the
/// cloud brain unavailable — which it currently is. A performance summary that disappears when a
/// key runs out is not a performance summary.
///
/// Everything here is counted, never characterised. No encouragement, no grade letter, no
/// "you're doing great": the numbers are the learner's own answers and they can read them. The
/// one judgement it makes is which areas to name as weak, and that is a count of wrong answers
/// grouped by the subtopic the question already declared.
/// </remarks>
public static class QuizPerformance
{
    /// <summary>A breakdown by some dimension of the questions — cognitive level, subtopic, type.</summary>
    public sealed record Band(string Name, int Correct, int Answered)
    {
        /// <summary>Null when nothing in this band was answered, rather than a misleading zero.</summary>
        public int? Percent => Answered == 0 ? null : (int)Math.Round(100.0 * Correct / Answered);
    }

    public sealed class Summary
    {
        public string QuizId { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string Difficulty { get; set; } = string.Empty;

        public int Answered { get; set; }
        public int Correct { get; set; }
        public int Total { get; set; }

        /// <summary>Null until at least one question has been answered.</summary>
        public int? ScorePercent { get; set; }

        public bool IsComplete { get; set; }

        /// <summary>Recall / Understanding / Application / ProblemSolving.</summary>
        public List<Band> ByCognitiveLevel { get; set; } = [];

        /// <summary>The subtopics the questions declared, where they declared one.</summary>
        public List<Band> BySubtopic { get; set; } = [];

        /// <summary>MCQ / ShortAnswer / TrueFalse / PracticalExercise / CaseStudy.</summary>
        public List<Band> ByQuestionType { get; set; } = [];

        /// <summary>Every question answered wrongly, with the explanation already attached to it.</summary>
        public List<Missed> Missed { get; set; } = [];

        /// <summary>
        /// Subtopics with more wrong than right. Named because they were counted, not diagnosed.
        /// </summary>
        public List<string> WeakAreas { get; set; } = [];

        /// <summary>The quiz's own next move, when it has one.</summary>
        public string? NextMove { get; set; }
    }

    public sealed class Missed
    {
        public string Question { get; set; } = string.Empty;
        public string? YourAnswer { get; set; }
        public string? CorrectAnswer { get; set; }
        public string? Explanation { get; set; }
        public string? Subtopic { get; set; }
        public string CognitiveLevel { get; set; } = string.Empty;
    }

    public static Summary Build(MentorQuiz quiz)
    {
        var questions = quiz.Questions ?? [];

        // Only questions the learner actually answered. Counting unanswered ones as wrong turns
        // an abandoned quiz into a bad result, which is a different thing.
        var answered = questions.Where(q => q.UserAnswered == true).ToList();

        var summary = new Summary
        {
            QuizId = quiz.Id,
            Topic = quiz.Topic,
            Difficulty = quiz.Difficulty,
            Total = questions.Count,
            Answered = answered.Count,
            Correct = answered.Count(q => q.IsCorrect == true),
            IsComplete = quiz.IsComplete,
            NextMove = string.IsNullOrWhiteSpace(quiz.NextMove) ? null : quiz.NextMove,
        };

        if (answered.Count > 0)
            summary.ScorePercent = (int)Math.Round(100.0 * summary.Correct / answered.Count);

        summary.ByCognitiveLevel = BandsBy(answered, q => Blank(q.CognitiveLevel));
        summary.BySubtopic = BandsBy(answered, q => Blank(q.Subtopic));
        summary.ByQuestionType = BandsBy(answered, q => Blank(q.QuestionType));

        summary.Missed = answered
            .Where(q => q.IsCorrect != true)
            .Select(q => new Missed
            {
                Question = q.Question,
                YourAnswer = AnswerText(q, q.UserAnswerIndex) ?? q.UserTextAnswer,
                CorrectAnswer = AnswerText(q, q.CorrectIndex),
                Explanation = string.IsNullOrWhiteSpace(q.Explanation) ? null : q.Explanation,
                Subtopic = Blank(q.Subtopic),
                CognitiveLevel = q.CognitiveLevel,
            })
            .ToList();

        // More wrong than right in a named subtopic. Two of three wrong is worth saying; one of
        // five is not, and calling it a weakness on that evidence is the kind of claim this
        // codebase keeps removing.
        summary.WeakAreas = summary.BySubtopic
            .Where(b => b.Answered > 0 && b.Correct * 2 < b.Answered)
            .OrderBy(b => b.Percent)
            .Select(b => b.Name)
            .ToList();

        return summary;
    }

    private static List<Band> BandsBy(List<MentorQuizQuestion> answered, Func<MentorQuizQuestion, string?> key) =>
        answered
            .Select(q => (Key: key(q), q.IsCorrect))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Band(g.Key, g.Count(x => x.IsCorrect == true), g.Count()))
            .OrderByDescending(b => b.Answered)
            .ToList();

    /// <summary>The option text at an index, or null when the index does not point at one.</summary>
    private static string? AnswerText(MentorQuizQuestion question, int? index)
    {
        var options = question.Options ?? [];
        return index is >= 0 && index < options.Count ? options[index.Value] : null;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
