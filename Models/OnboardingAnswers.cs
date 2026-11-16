namespace RadarV2.Models;

/// <summary>
/// Turning what somebody picked into what Radar can match on.
/// </summary>
/// <remarks>
/// The three questions have been served since they were written and no answer could reach a
/// profile: there was no transport for the capabilities question at all, and the two single
/// choices - a qualification and a seniority - had nowhere to land even once there was.
///
/// This is deliberately a translation rather than a copy. Three of the answers are not subjects
/// and must never be matched on, two of them are positions on a ladder that have to become
/// typed values, and one of them is not a seniority at all. Storing the strings as given would
/// put "None yet" and "Other" into the terms the feed is selected by.
/// </remarks>
public static class OnboardingAnswers
{
    /// <summary>
    /// Answers that are real but are not subjects.
    /// </summary>
    /// <remarks>
    /// Each is a true thing to tell Radar and a terrible thing to search for. "Other" with no
    /// free-text box beside it is an opt-out; matched as a term it hits "and other" in a large
    /// share of headlines and widens the feed back towards everything, which is the exact failure
    /// the interest matching was narrowed to remove.
    ///
    /// The two not-yet answers are worse, because the matcher splits on words: "None yet - I'm
    /// exploring" yields "im", which prefix-matches important, impact, implementation and
    /// immigration. That is the same shape as the "don't" to "Donald" bug this codebase has
    /// already been burned by once.
    ///
    /// They are kept on the profile and excluded from the terms. A person who said they are
    /// still exploring has told Radar something; it just is not a search query.
    /// </remarks>
    public static readonly IReadOnlySet<string> NotSubjects =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Other",
            "None yet — I'm exploring",
            "I just want to learn and stay informed",
        };

    /// <summary>Whether this answer should reach the matching terms.</summary>
    public static bool IsMatchable(string answer) =>
        !string.IsNullOrWhiteSpace(answer) && !NotSubjects.Contains(answer.Trim());

    /// <summary>
    /// The qualification ladder, as the question words it.
    /// </summary>
    /// <remarks>
    /// "Professional Certification" is deliberately absent. It is offered on the question and is
    /// a true answer, but it is not a rung: it sat above Doctorate once and made the floor
    /// comparison wrong in both directions. <see cref="Read"/> routes it to the certification
    /// list instead.
    ///
    /// "Other" is absent too. Somebody whose qualification is not on the list has not told us
    /// where they sit, and guessing a rung for them is worse than leaving it unstated.
    /// </remarks>
    private static readonly Dictionary<string, QualificationLevel> Ladder =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["HND"] = QualificationLevel.Hnd,
            ["Bachelor's Degree"] = QualificationLevel.Bachelors,
            ["Master's Degree"] = QualificationLevel.Masters,
            ["PhD"] = QualificationLevel.Doctorate,
        };

    /// <summary>
    /// The seniority ladder, as the professional question words it.
    /// </summary>
    /// <remarks>
    /// "Entrepreneur / Founder" is absent for the same reason a certification is absent above: it
    /// is not more senior than a director or less than a manager, it is a different axis, and
    /// <see cref="Read"/> sets <see cref="UserProfile.IsFounder"/> for it instead.
    /// </remarks>
    private static readonly Dictionary<string, Seniority> Levels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Entry-level"] = Seniority.Entry,
            ["Mid-level"] = Seniority.Mid,
            ["Senior-level"] = Seniority.Senior,
            ["Manager"] = Seniority.Manager,
            ["Director / Executive"] = Seniority.Director,
        };

    /// <summary>The answer that says somebody runs their own thing.</summary>
    public const string Founder = "Entrepreneur / Founder";

    /// <summary>The qualification answer that is a certificate rather than a rung.</summary>
    public const string Certification = "Professional Certification";

    /// <summary>
    /// Applies a validated set of answers to a profile.
    /// </summary>
    /// <remarks>
    /// Takes answers keyed by the field id the question was served under, so what is written is
    /// tied to what was asked rather than to the order a client happened to send. A field that is
    /// absent leaves its profile value alone: not answering a question is not the same as
    /// answering it with nothing, and a client that sends two of three must not wipe the third.
    /// </remarks>
    public static void Read(UserProfile profile, IReadOnlyDictionary<string, List<string>> answers)
    {
        if (answers.TryGetValue("interests", out var interests))
            profile.Interests = [.. interests.Where(IsMatchable)];

        if (answers.TryGetValue("skills", out var skills))
            profile.Capabilities = [.. skills.Where(IsMatchable)];

        if (answers.TryGetValue("goals", out var goals))
            profile.Goals = [.. goals.Where(IsMatchable)];

        if (answers.TryGetValue("qualification", out var qualification))
        {
            var stated = qualification.FirstOrDefault();

            if (stated is not null && Ladder.TryGetValue(stated, out var level))
            {
                profile.Qualification = level;
            }
            else if (string.Equals(stated, Certification, StringComparison.OrdinalIgnoreCase))
            {
                // A certificate is evidence of a skill, not a position on the academic ladder.
                // Recorded where Eligibility.RequiredCertifications can be compared against it.
                profile.Certifications = [.. profile.Certifications.Append(Certification)
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
            }
        }

        if (answers.TryGetValue("level", out var level2))
        {
            var stated = level2.FirstOrDefault();

            if (stated is not null && Levels.TryGetValue(stated, out var seniority))
            {
                profile.SeniorityLevel = seniority;
            }
            else if (string.Equals(stated, Founder, StringComparison.OrdinalIgnoreCase))
            {
                // Says nothing about seniority, so SeniorityLevel stays as it was - which for a
                // first answer is null, meaning unstated, which is accurate.
                profile.IsFounder = true;
            }
        }
    }
}
