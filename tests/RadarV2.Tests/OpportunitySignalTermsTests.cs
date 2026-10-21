using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

public class OpportunitySignalTermsTests
{
    private static Opportunity Make(string title, string description = "", string goalTag = "Career",
        OpportunityType type = OpportunityType.Job) =>
        new() { Title = title, Description = description, GoalTag = goalTag, Type = type };

    [Fact]
    public void Learns_the_category_not_the_one_opportunity()
    {
        // The title identifies this row and transfers to nothing. Marking it "not relevant" should
        // teach Radar about the kind of thing, or it has learned nothing usable.
        var terms = OpportunitySignalTerms.For(
            Make("Google Cybersecurity Professional Fellowship", "SOC analyst track", "Career", OpportunityType.Fellowship));

        Assert.DoesNotContain(terms, t => t.Contains("Google", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Fellowship", terms);
        Assert.Contains("Career", terms);
        Assert.Contains("cyber", terms);
    }

    [Fact]
    public void An_unlisted_field_teaches_no_field_rather_than_the_wrong_one()
    {
        // The failure this guards: an opportunity Radar cannot place being filed under whichever
        // field happened to match loosely, which is how social work became cybersecurity.
        var terms = OpportunitySignalTerms.For(
            Make("Graduate Nurse Residency", "Clinical placement at a teaching hospital", "Education", OpportunityType.Internship));

        Assert.Contains("Internship", terms);
        Assert.Contains("Education", terms);
        Assert.DoesNotContain("cyber", terms);
        Assert.DoesNotContain("data", terms);
        Assert.DoesNotContain("software", terms);
    }

    [Fact]
    public void Social_work_is_not_filed_as_cybersecurity()
    {
        var terms = OpportunitySignalTerms.For(
            Make("Social Work Bursary", "Community development placement", "Education"));

        Assert.DoesNotContain("cyber", terms);
    }

    [Fact]
    public void Stays_short_enough_that_signal_is_not_drowned_in_noise()
    {
        // Learning every word of a description teaches noise, and noise outvotes signal once
        // there is enough of it.
        var terms = OpportunitySignalTerms.For(
            Make("Security Engineer", new string('x', 4000) + " cybersecurity threat detection", "Career"));

        Assert.InRange(terms.Count, 1, 4);
    }

    [Fact]
    public void Never_repeats_a_term()
    {
        var terms = OpportunitySignalTerms.For(Make("Job", "work", "Job"));

        Assert.Equal(terms.Count, terms.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_missing_goal_tag_is_skipped_rather_than_recorded_blank()
    {
        var terms = OpportunitySignalTerms.For(Make("Something", "anything", goalTag: "   "));

        Assert.All(terms, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }
}
