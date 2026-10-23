using RadarV2.Models;
using RadarV2.Services;

namespace RadarV2.Tests;

/// <summary>
/// An interest that maps to no layer, or to the wrong one, gets the whole feed.
///
/// `BuildFeedFilterAsync` narrows the feed to the layers a learner's interests map to, and falls
/// back to everything when they map to nothing. So an unmapped subject does not fail loudly — it
/// quietly returns ESPN, Variety and Billboard, which is what a civil engineering student was
/// actually being shown. Measured: seven of sixteen common subjects returned nothing relevant.
/// </summary>
public class InterestLayerMapperTests
{
    private static List<ContentLayer> Map(params string[] interests) =>
        InterestLayerMapper.MapInterestsToLayers([.. interests]);

    // The practitioner layer the new computing sources live in. Without it, the sources added
    // specifically for these subjects would have been filtered out of their own learners' feeds.
    [Theory]
    [InlineData("Computer Science")]
    [InlineData("Software Engineering")]
    [InlineData("Web Development")]
    public void ComputingReachesTheSkillsLayer(string interest)
    {
        Assert.Contains(ContentLayer.Skills, Map(interest));
    }

    [Theory]
    [InlineData("Civil Engineering")]
    [InlineData("Mechanical Engineering")]
    [InlineData("Electrical Engineering")]
    public void EngineeringReachesIndustryAndSkills(string interest)
    {
        var layers = Map(interest);
        Assert.Contains(ContentLayer.Industry, layers);
        Assert.Contains(ContentLayer.Skills, layers);
    }

    [Fact]
    public void PharmacyAndNursingReachMedicine()
    {
        Assert.Contains(ContentLayer.Medicine, Map("Pharmacy"));
        Assert.Contains(ContentLayer.Medicine, Map("Nursing"));
    }

    // Data science is a practice as well as a literature; mapping it to research alone sent a
    // learner to papers and never to anything they could follow along with.
    [Fact]
    public void DataScienceReachesPracticeAsWellAsResearch()
    {
        var layers = Map("Data Science");
        Assert.Contains(ContentLayer.Skills, layers);
        Assert.Contains(ContentLayer.Science, layers);
    }

    [Fact]
    public void AccountingStillReachesFinance()
    {
        Assert.Contains(ContentLayer.Finance, Map("Accounting"));
    }

    // The guard that matters most: no common subject may map to nothing, because nothing means
    // the unfiltered feed.
    [Theory]
    [InlineData("Computer Science")]
    [InlineData("Software Engineering")]
    [InlineData("Civil Engineering")]
    [InlineData("Mechanical Engineering")]
    [InlineData("Accounting")]
    [InlineData("Pharmacy")]
    [InlineData("Data Science")]
    [InlineData("Nursing")]
    [InlineData("Medicine")]
    [InlineData("Law")]
    [InlineData("Economics")]
    [InlineData("Agriculture")]
    public void EveryCommonSubjectMapsSomewhere(string interest)
    {
        Assert.NotEmpty(Map(interest));
    }

    [Fact]
    public void NoInterestsMeansNoNarrowing()
    {
        Assert.Empty(Map());
    }
}
