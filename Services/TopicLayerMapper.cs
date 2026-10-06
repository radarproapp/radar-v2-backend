using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Derives an item's coarse <see cref="ContentLayer"/> from its own topics/tags instead of blindly
/// inheriting the source's layer. A general outlet (e.g. Premium Times) is registered under one
/// layer for the registry's convenience, but that must not make every article it publishes appear
/// under that heading — a blue-finance piece should read as Finance/Environment, not Law.
///
/// Reuses <see cref="InterestLayerMapper"/> so the content→layer vocabulary can never drift from
/// the interest→layer vocabulary the feed filters on.
/// </summary>
public static class TopicLayerMapper
{
    public static ContentLayer? Map(IEnumerable<string> topics)
    {
        foreach (var topic in topics)
        {
            if (string.IsNullOrWhiteSpace(topic)) continue;
            var layers = InterestLayerMapper.MapInterestsToLayers([topic]);
            if (layers.Count > 0) return layers[0];
        }
        return null;
    }
}
