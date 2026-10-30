using MongoDB.Driver;
using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Which of the two products a piece of content belongs to.
/// </summary>
/// <remarks>
/// Radar Intelligence is what is happening: the news, the podcasts, the posts people publish
/// about their field this week. Research Discovery is what has been established: papers, policy
/// documents, essays and long-form analysis. They had been pouring into one list, so somebody
/// opening the feed over breakfast got a working paper from a think tank between two news items,
/// and somebody looking for the working paper had to scroll a news feed to find it.
///
/// The split is by what the thing is, not by how good it is. A news report and a policy paper on
/// the same subject are both worth reading; they are not worth reading at the same moment.
///
/// One definition, in one place, because the two surfaces have to agree. If the feed excludes a
/// type that Research Discovery does not include, that content exists in the database and appears
/// nowhere - which is the failure mode that is impossible to see from either screen.
/// </remarks>
public static class ContentSurface
{
    /// <summary>
    /// What Radar Intelligence carries: news, podcasts and posts.
    /// </summary>
    /// <remarks>
    /// Video is not here. It is not news, and a lesson is where somebody watches something -
    /// Radar Learn asks for videos by subject rather than showing whatever arrived today.
    /// </remarks>
    public static readonly ContentType[] Intelligence =
    [
        ContentType.Article,
        ContentType.Podcast,
    ];

    /// <summary>
    /// What Research Discovery carries: papers, policy, essays and the long-form analysis that
    /// comes from an academic, policy or ideas source.
    /// </summary>
    public static readonly ContentType[] Research =
    [
        ContentType.ResearchPaper,
        ContentType.PolicyPaper,
        ContentType.Essay,
        ContentType.Book,
        ContentType.Documentation,
        ContentType.Framework,
    ];

    /// <summary>
    /// The layers whose written work is research even when it is filed as an article.
    /// </summary>
    /// <remarks>
    /// A think tank publishes its analysis through the same RSS feed as its blog, so the type
    /// alone cannot tell them apart - 161 of the sources in the registry default to Article,
    /// including every policy institute and university. The layer is the thing the source was
    /// registered as, and it is the only classification in the data that was made deliberately.
    /// </remarks>
    public static readonly ContentLayer[] ResearchLayers =
    [
        ContentLayer.Policy,
        ContentLayer.Academic,
        ContentLayer.Ideas,
    ];

    /// <summary>Whether this belongs in the Intelligence feed.</summary>
    public static bool IsIntelligence(ContentItem item) =>
        Intelligence.Contains(item.Type) && !ResearchLayers.Contains(item.Layer);

    /// <summary>Whether this belongs in Research Discovery.</summary>
    /// <remarks>
    /// The exact complement of the above for everything except video, so nothing written can fall
    /// between the two.
    /// </remarks>
    public static bool IsResearch(ContentItem item) =>
        Research.Contains(item.Type)
        || (Intelligence.Contains(item.Type) && ResearchLayers.Contains(item.Layer));

    /// <summary>
    /// Every layer that is not a research one, named rather than excluded.
    /// </summary>
    /// <remarks>
    /// $nin matches a document that has no Layer field at all, and production holds documents
    /// that predate fields. So the query admitted a layerless article to the feed while the
    /// predicate, reading the model's default of Ideas, called the same document research.
    /// Inverted, on the one case nobody writes a fixture for.
    ///
    /// Listing what is admitted means a missing field matches neither surface - which is still
    /// wrong, but it is the same wrong on both sides, and the test that every type lands on
    /// exactly one surface is then telling the truth about the queries too.
    /// </remarks>
    private static readonly ContentLayer[] FeedLayers =
        [.. Enum.GetValues<ContentLayer>().Where(layer => !ResearchLayers.Contains(layer))];

    /// <summary>The feed's half of the split, as a query.</summary>
    public static FilterDefinition<ContentItem> IntelligenceFilter() =>
        Builders<ContentItem>.Filter.In(i => i.Type, Intelligence)
        & Builders<ContentItem>.Filter.In(i => i.Layer, FeedLayers);

    /// <summary>Research Discovery's half, as a query.</summary>
    /// <remarks>
    /// A document with no Layer field counts as research, because that is what the predicate
    /// says: the model defaults the field to Ideas, so anything stored before the field existed
    /// reads as a research layer once it is loaded. Without the Exists clause the query
    /// disagreed - and since the feed query lists the layers it admits, neither side claimed it.
    /// A legacy article was then ingested, stored, and reachable from no screen at all, which is
    /// the one failure this class exists to prevent.
    /// </remarks>
    public static FilterDefinition<ContentItem> ResearchFilter() =>
        Builders<ContentItem>.Filter.In(i => i.Type, Research)
        | (Builders<ContentItem>.Filter.In(i => i.Type, Intelligence)
           & (Builders<ContentItem>.Filter.In(i => i.Layer, ResearchLayers)
              | Builders<ContentItem>.Filter.Exists(i => i.Layer, false)));
}
