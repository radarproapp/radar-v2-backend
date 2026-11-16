using RadarV2.Models;
using RadarV2.Services.Ingestion;

namespace RadarV2.Tests;

/// <summary>
/// The eight families, the eligibility nobody could match against, and the rights nobody checked.
/// </summary>
/// <remarks>
/// Three things the opportunity engine could not do, each of which looked like a small omission
/// and was not: there was no grouping above the seventeen stored types, so the third onboarding
/// question had no sensible list to offer; eligibility existed only as sentences, so nothing
/// could compare a posting against a person; and a source had nowhere to say what Radar was
/// permitted to publish from it.
/// </remarks>
public class OpportunityTaxonomyTests
{
    // ── the families ─────────────────────────────────────────────────────────

    /// <summary>
    /// Every stored type is filed deliberately.
    /// </summary>
    /// <remarks>
    /// The mapping falls back to Employment rather than throwing, so a forgotten type renders
    /// wrongly instead of breaking a feed. That is the right behaviour in production and makes a
    /// mistake invisible, so this is the test that actually catches one: it names each type
    /// explicitly, and adding an eighteenth fails here until somebody decides where it belongs.
    /// </remarks>
    [Theory]
    [InlineData(OpportunityType.Job, OpportunityFamily.EmploymentAndIncome)]
    [InlineData(OpportunityType.Internship, OpportunityFamily.EmploymentAndIncome)]
    [InlineData(OpportunityType.RemoteJob, OpportunityFamily.EmploymentAndIncome)]
    [InlineData(OpportunityType.GraduateProgramme, OpportunityFamily.EmploymentAndIncome)]
    [InlineData(OpportunityType.Scholarship, OpportunityFamily.EducationAndDevelopment)]
    [InlineData(OpportunityType.Training, OpportunityFamily.EducationAndDevelopment)]
    [InlineData(OpportunityType.Bootcamp, OpportunityFamily.EducationAndDevelopment)]
    [InlineData(OpportunityType.Funding, OpportunityFamily.FundingAndEnterprise)]
    [InlineData(OpportunityType.StartupAccelerator, OpportunityFamily.FundingAndEnterprise)]
    [InlineData(OpportunityType.Competition, OpportunityFamily.FundingAndEnterprise)]
    [InlineData(OpportunityType.Event, OpportunityFamily.EventsAndNetworking)]
    [InlineData(OpportunityType.Conference, OpportunityFamily.EventsAndNetworking)]
    [InlineData(OpportunityType.Workshop, OpportunityFamily.EventsAndNetworking)]
    [InlineData(OpportunityType.Seminar, OpportunityFamily.EventsAndNetworking)]
    [InlineData(OpportunityType.JobFair, OpportunityFamily.EventsAndNetworking)]
    [InlineData(OpportunityType.ResearchGrant, OpportunityFamily.ResearchAndKnowledge)]
    [InlineData(OpportunityType.Fellowship, OpportunityFamily.LeadershipAndRecognition)]
    public void EveryTypeIsFiledUnderAFamilyOnPurpose(OpportunityType type, OpportunityFamily family) =>
        Assert.Equal(family, OpportunityFamilies.Of(type));

    [Fact]
    public void TheTheoryAboveCoversEveryTypeThatExists()
    {
        var covered = typeof(OpportunityTaxonomyTests)
            .GetMethod(nameof(EveryTypeIsFiledUnderAFamilyOnPurpose))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .Select(data => (OpportunityType)data.GetData(null!).First()[0]!)
            .ToHashSet();

        var missing = Enum.GetValues<OpportunityType>().Where(type => !covered.Contains(type)).ToList();

        Assert.True(missing.Count == 0, $"Not filed under any family: {string.Join(", ", missing)}");
    }

    /// <summary>A family a person has to choose from says what it covers.</summary>
    /// <remarks>
    /// Somebody picking "Business &amp; Institutional" has to be able to tell it means tenders
    /// and procurement rather than business news, without guessing.
    /// </remarks>
    [Fact]
    public void EveryFamilyIsNamedAndExplained()
    {
        Assert.Equal(8, OpportunityFamilies.All.Count);

        foreach (var family in OpportunityFamilies.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(OpportunityFamilies.Label(family)));
            Assert.False(string.IsNullOrWhiteSpace(OpportunityFamilies.Covers(family)));
        }
    }

    // ── eligibility ──────────────────────────────────────────────────────────

    /// <summary>
    /// Nothing stated is not the same as open to everybody.
    /// </summary>
    /// <remarks>
    /// The distinction a screen claiming to check eligibility rests on. A source that did not say
    /// who may apply has told us nothing, and a caller that cannot tell silence from permission
    /// will assert something nothing established.
    /// </remarks>
    [Fact]
    public void AnOpportunityWithNoStatedCriteriaSaysSo()
    {
        Assert.False(new Eligibility().WasStated);
        Assert.False(new Opportunity().Eligibility.WasStated);
    }

    [Fact]
    public void AnyOneStatedCriterionCountsAsStated()
    {
        Assert.True(new Eligibility { Nationalities = ["Nigerian"] }.WasStated);
        Assert.True(new Eligibility { MinimumQualification = QualificationLevel.Bachelors }.WasStated);
        Assert.True(new Eligibility { MinimumYearsExperience = 3 }.WasStated);
        Assert.True(new Eligibility { RequiredSkills = ["Research"] }.WasStated);
        Assert.True(new Eligibility { MaximumAge = 30 }.WasStated);
    }

    /// <summary>
    /// Qualifications compare, which is what lets "a degree or higher" be checked.
    /// </summary>
    /// <remarks>
    /// The ordering is the whole reason this is an enum rather than a string. It is also why the
    /// values must only ever be appended: they persist by value, so inserting one in the middle
    /// would silently relabel every record already stored.
    /// </remarks>
    [Fact]
    public void QualificationsAreOrderedSoAFloorCanBeChecked()
    {
        Assert.True(QualificationLevel.Masters > QualificationLevel.Bachelors);
        Assert.True(QualificationLevel.Bachelors > QualificationLevel.Hnd);
        Assert.True(QualificationLevel.Hnd > QualificationLevel.Ond);
        Assert.True(QualificationLevel.Doctorate > QualificationLevel.Masters);
        Assert.True(QualificationLevel.InProgress < QualificationLevel.SecondarySchool);
    }

    // ── source rights ────────────────────────────────────────────────────────

    /// <summary>
    /// A source nobody has checked is not a source nobody objects to.
    /// </summary>
    /// <remarks>
    /// Unconfirmed is the first value so an uninitialised field reads as "unknown" rather than as
    /// permission, and it does not permit publishing.
    /// </remarks>
    [Fact]
    public void UnconfirmedRightsAreTheDefaultAndDoNotPermitPublishing()
    {
        Assert.Equal(Redistribution.Unconfirmed, default(Redistribution));
        Assert.Equal(Redistribution.Unconfirmed, new Opportunity().Rights);
    }

    [Theory]
    [InlineData(Redistribution.Permitted, true, true)]
    [InlineData(Redistribution.LinkOnly, true, false)]
    [InlineData(Redistribution.Unconfirmed, false, false)]
    [InlineData(Redistribution.DoNotPublish, false, false)]
    public void WhatMayBeShownFollowsTheRecordedRights(Redistribution rights, bool mayPublish, bool mayStoreDescription)
    {
        var feed = new OpportunitySourceRegistry.OpportunityFeed(
            "A Source", "https://example.com/feed", "Nigeria",
            OpportunityType.Job, "Career", SourceAccess.Feed, rights);

        Assert.Equal(mayPublish, feed.MayPublish);
        Assert.Equal(mayStoreDescription, feed.MayStoreDescription);
    }

    /// <summary>
    /// Every source in the register states a position, even if that position is "unchecked".
    /// </summary>
    /// <remarks>
    /// The field has no default for exactly this reason: adding a source without saying what may
    /// be done with its listings should be something you notice while writing the line. Six were
    /// ingested and rendered for months under an assumption nobody had written down.
    /// </remarks>
    [Fact]
    public void EverySourceInTheRegisterSaysWhatMayBeDoneWithIt()
    {
        Assert.NotEmpty(OpportunitySourceRegistry.All);

        foreach (var feed in OpportunitySourceRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(feed.Name));

            // Unconfirmed is allowed and is the honest answer today. A blank note is not: if
            // nobody has checked, the row should say what still needs checking.
            Assert.False(
                string.IsNullOrWhiteSpace(feed.Note),
                $"{feed.Name} records no note about its rights position.");
        }
    }

    /// <summary>Each source is filed under a family too, through its type.</summary>
    [Fact]
    public void EverySourceFallsUnderAFamily()
    {
        foreach (var feed in OpportunitySourceRegistry.All)
            Assert.Contains(feed.Family, OpportunityFamilies.All);
    }
}
