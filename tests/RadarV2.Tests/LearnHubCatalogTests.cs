using RadarV2.Services;

namespace RadarV2.Tests;

public class LearnHubCatalogTests
{
    [Theory]
    [InlineData("Nursing")]
    [InlineData("Commercial law")]
    [InlineData("Fashion design")]
    [InlineData("Hair styling")]
    [InlineData("Retail management")]
    [InlineData("Teacher training")]
    public void An_uncurated_subject_does_not_get_the_cybersecurity_hub(string subject)
    {
        // The old fallback was "return BuildCybersecurityPayload(...)", so every subject the
        // catalogue did not recognise was shown SOC analyst resources and a cybersecurity
        // knowledge graph. "Hair styling", "training" and "retail" also matched the AI hub,
        // because that branch tested Contains("ai").
        var payload = LearnHubCatalog.GetPayload(subject);

        Assert.DoesNotContain("Cybersecurity", payload.PrimarySkill, StringComparison.OrdinalIgnoreCase);
        Assert.All(payload.Resources, r =>
            Assert.DoesNotContain("cyber", r.PrimarySkill, StringComparison.OrdinalIgnoreCase));
        Assert.All(payload.GraphNodes, n =>
            Assert.DoesNotContain("cyber", n.Id, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_uncurated_subject_is_named_after_itself()
    {
        var payload = LearnHubCatalog.GetPayload("Nursing");

        Assert.Contains("Nursing", payload.PrimarySkill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nursing", payload.NextAction.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_uncurated_subject_borrows_no_resources()
    {
        // Empty is the honest answer. Listing somebody else's reading list is worse than listing
        // none, because the user cannot tell it is not theirs.
        var payload = LearnHubCatalog.GetPayload("Commercial law");

        Assert.Empty(payload.Resources);
        Assert.Empty(payload.GraphNodes);
    }

    [Fact]
    public void A_user_who_has_said_nothing_is_asked_rather_than_assigned_a_field()
    {
        var payload = LearnHubCatalog.GetPayload("");

        Assert.Equal(string.Empty, payload.PrimarySkill);
        Assert.Contains("what you want to learn", payload.NextAction.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Cybersecurity")]
    [InlineData("SOC analyst")]
    [InlineData("digital forensics")]
    public void A_cybersecurity_learner_still_gets_the_cybersecurity_hub(string subject)
    {
        var payload = LearnHubCatalog.GetPayload(subject);

        Assert.Contains("Cybersecurity", payload.PrimarySkill, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(payload.Resources);
    }

    [Fact]
    public void A_social_work_learner_is_not_routed_to_cybersecurity()
    {
        var payload = LearnHubCatalog.GetPayload("Social work");

        Assert.DoesNotContain("Cybersecurity", payload.PrimarySkill, StringComparison.OrdinalIgnoreCase);
    }
}
