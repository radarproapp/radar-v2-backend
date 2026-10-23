using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// The three titles in this first test are real. They are what a Computer Science student was
/// actually shown when they opened their path, and they are the reason the path felt like filler:
/// each one is the subject's name with a generic frame around it.
/// </summary>
public class LessonTitleQualityTests
{
    [Theory]
    [InlineData("What Computer Science actually involves day to day")]
    [InlineData("Core concepts and vocabulary in Computer Science")]
    [InlineData("The standard toolset for Computer Science")]
    public void RejectsWhatTheStudentWasActuallyShown(string title)
    {
        Assert.True(LessonTitleQuality.IsGeneric(title, "Computer Science"));
    }

    [Theory]
    [InlineData("Introduction to Cybersecurity")]
    [InlineData("Intro to Cybersecurity")]
    [InlineData("Core concepts of Cybersecurity")]
    [InlineData("Applying Cybersecurity in practice")]
    [InlineData("Getting started with Cybersecurity")]
    [InlineData("Cybersecurity fundamentals")]
    [InlineData("Overview of Cybersecurity")]
    [InlineData("Understanding Cybersecurity")]
    [InlineData("Foundations of Cybersecurity")]
    public void RejectsTheSubjectNameInACostume(string title)
    {
        Assert.True(LessonTitleQuality.IsGeneric(title, "Cybersecurity"));
    }

    // A lesson whose entire title is the subject teaches nothing by its name either.
    [Fact]
    public void RejectsATitleThatIsOnlyTheSubject()
    {
        Assert.True(LessonTitleQuality.IsGeneric("Cybersecurity", "Cybersecurity"));
        Assert.True(LessonTitleQuality.IsGeneric("  Nursing  ", "Nursing"));
    }

    [Theory]
    [InlineData("Reading a packet capture in Wireshark")]
    [InlineData("Writing a SIEM detection rule for failed logins")]
    [InlineData("Threat modelling a login flow")]
    public void KeepsATitleThatNamesSomething(string title)
    {
        Assert.False(LessonTitleQuality.IsGeneric(title, "Cybersecurity"));
    }

    // The subject appears, but so does real content. These are the ones a blunter rule would eat.
    [Theory]
    [InlineData("Cybersecurity risk registers for a small team")]
    [InlineData("How Cybersecurity budgets are actually approved")]
    [InlineData("Core Cybersecurity controls in ISO 27001")]
    public void KeepsATitleThatMentionsTheSubjectAndStillSaysSomething(string title)
    {
        Assert.False(LessonTitleQuality.IsGeneric(title, "Cybersecurity"));
    }

    // Conservative by design: with nothing to strip there is no claim to make, so an unrelated
    // title is never rejected on these grounds.
    [Fact]
    public void NeverJudgesATitleThatDoesNotMentionItsSubject()
    {
        Assert.False(LessonTitleQuality.IsGeneric("Introduction to the basics", "Cybersecurity"));
    }

    [Fact]
    public void TreatsAnEmptyTitleAsGenericAndAnEmptySubjectAsUnjudgeable()
    {
        Assert.True(LessonTitleQuality.IsGeneric("", "Cybersecurity"));
        Assert.True(LessonTitleQuality.IsGeneric(null, "Cybersecurity"));
        Assert.False(LessonTitleQuality.IsGeneric("Introduction to Cybersecurity", ""));
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        Assert.True(LessonTitleQuality.IsGeneric("INTRODUCTION TO cybersecurity", "Cybersecurity"));
    }

    [Fact]
    public void RemoveGenericKeepsOnlyTheLessonsThatNameSomething()
    {
        string[] titles =
        [
            "Introduction to Data Science",
            "Cleaning a messy CSV with pandas",
            "Core concepts of Data Science",
            "Choosing between a t-test and a chi-square test",
        ];

        var kept = LessonTitleQuality.RemoveGeneric(titles, "Data Science", t => t);

        Assert.Equal(
            ["Cleaning a messy CSV with pandas", "Choosing between a t-test and a chi-square test"],
            kept);
    }
}
