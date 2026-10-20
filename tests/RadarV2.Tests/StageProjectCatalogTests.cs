using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// Every learner gets a project per stage, whatever their field. The catalogue exists because
/// scattered keyword branches meant a domain nobody anticipated got another domain's curriculum,
/// or nothing at all.
/// </summary>
public class StageProjectCatalogTests
{
    private static readonly string[] Stages = ["Beginner", "Intermediate", "Advanced", "Application"];

    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("Data analytics")]
    [InlineData("Software engineering")]
    // Domains with no dedicated entry must still get a real, specific project.
    [InlineData("hospital nurse management")]
    [InlineData("agricultural extension work")]
    [InlineData("constitutional law")]
    public void Every_subject_gets_a_project_at_every_stage(string subject)
    {
        foreach (var stage in Stages)
        {
            var project = StageProjectCatalog.For(stage, subject, capability: null);

            Assert.False(string.IsNullOrWhiteSpace(project.Title), $"{subject}/{stage}: no title");
            Assert.False(string.IsNullOrWhiteSpace(project.Deliverable), $"{subject}/{stage}: no deliverable");
            Assert.False(string.IsNullOrWhiteSpace(project.WhyItProvesCapability), $"{subject}/{stage}: no rationale");
            Assert.NotEmpty(project.Steps);
        }
    }

    [Fact]
    public void A_cybersecurity_learner_builds_security_work()
    {
        var project = StageProjectCatalog.For("Intermediate", "Cybersecurity", capability: "Threat detection");

        Assert.Contains("SIEM", project.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unlisted_domain_does_not_borrow_another_domains_project()
    {
        // The original bug: a nurse got the data-analytics curriculum because her goal matched no
        // keyword and the default was somebody else's path.
        var nursing = StageProjectCatalog.For("Beginner", "hospital nurse management", capability: null);

        Assert.DoesNotContain("SIEM", nursing.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Excel", nursing.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dataset", nursing.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generic_project_titles_read_as_english()
    {
        // "Try Become a hospital nurse manager Nursing on something small" shipped once.
        var project = StageProjectCatalog.For("Beginner", "hospital nurse management", capability: null);

        Assert.DoesNotContain("Become a", project.Title);
        Assert.DoesNotContain("  ", project.Title);
    }

    [Fact]
    public void An_unknown_stage_name_still_returns_a_project()
    {
        var project = StageProjectCatalog.For("NotAStage", "Cybersecurity", capability: null);
        Assert.False(string.IsNullOrWhiteSpace(project.Title));
    }
}
