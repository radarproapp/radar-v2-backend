using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using RadarV2.Data;
using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// A field removed from a model must not break reads of the documents written while it existed.
///
/// The failure this reproduces was live on staging: <c>/api/home</c> returned 500 for every
/// profile with interests, because content_items written before <c>YourEdge</c> was removed from
/// ContentItem still carry it, and the driver's default is to throw on an element it cannot map.
/// A field deleted weeks earlier was breaking a page today.
/// </summary>
public class BsonConventionTests
{
    public BsonConventionTests() => BsonConventions.Register();

    [Fact]
    public void ReadsADocumentCarryingAFieldTheModelNoLongerHas()
    {
        var stored = new BsonDocument
        {
            { "_id", "abc" },
            { "Title", "Zero trust is not a product" },
            { "Source", "Example" },
            // Removed from ContentItem. Present on every document written before it went.
            { "YourEdge", "a line that used to be generated here" },
        };

        var item = BsonSerializer.Deserialize<ContentItem>(stored);

        Assert.Equal("abc", item.Id);
        Assert.Equal("Zero trust is not a product", item.Title);
    }

    // Not ContentItem's problem alone: none of the persisted models had this, so each was one
    // removed property away from the same failure. The convention is registered by namespace.
    [Theory]
    [InlineData(typeof(UserProfile))]
    [InlineData(typeof(Opportunity))]
    [InlineData(typeof(GrowthRoadmap))]
    [InlineData(typeof(Note))]
    public void AppliesToEveryModelNotJustTheOneThatBroke(Type modelType)
    {
        var stored = new BsonDocument
        {
            { "_id", "abc" },
            { "AFieldThisModelNeverHad", "value" },
        };

        var deserialized = BsonSerializer.Deserialize(stored, modelType);

        Assert.NotNull(deserialized);
    }

    [Fact]
    public void StillReadsTheFieldsTheModelDoesHave()
    {
        var stored = new BsonDocument
        {
            { "_id", "n1" },
            { "Title", "Kept" },
            { "SomethingRetired", 42 },
        };

        var note = BsonSerializer.Deserialize<Note>(stored);

        Assert.Equal("n1", note.Id);
        Assert.Equal("Kept", note.Title);
    }
}
