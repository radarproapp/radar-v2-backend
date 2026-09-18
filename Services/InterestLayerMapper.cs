using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Single source of truth for mapping onboarding interest strings to ContentLayer values.
/// Previously copy-pasted (and drifted out of sync) across MongoIntelligenceFeedService,
/// MongoNavigatorService and MongoWeeklyBriefService — kept here once so every surface that
/// personalises by interest (feed, today, weekly brief) stays consistent.
/// </summary>
public static class InterestLayerMapper
{
    public static List<ContentLayer> MapInterestsToLayers(List<string> interests)
    {
        if (interests.Count == 0) return [];

        var layers = new HashSet<ContentLayer>();
        foreach (var interest in interests)
        {
            var norm = interest.Trim().ToLowerInvariant();
            ContentLayer[] mapped = norm switch
            {
                var s when s.Contains("tech") || s.Contains("ai") || s.Contains("software")
                    => [ContentLayer.Ideas, ContentLayer.Science],
                var s when s.Contains("business") || s.Contains("entrepreneur") || s.Contains("startup")
                    => [ContentLayer.Ideas, ContentLayer.Finance, ContentLayer.Career],
                var s when s.Contains("finance") || s.Contains("invest") || s.Contains("money") || s.Contains("banking")
                    => [ContentLayer.Finance],
                var s when s.Contains("policy") || s.Contains("governance") || s.Contains("politi")
                    => [ContentLayer.Policy],
                var s when s.Contains("health") || s.Contains("medicine") || s.Contains("medical")
                    => [ContentLayer.Medicine],
                var s when s.Contains("climate") || s.Contains("environment") || s.Contains("green")
                    => [ContentLayer.Environment],
                var s when s.Contains("science") || s.Contains("research") || s.Contains("academic")
                    => [ContentLayer.Science, ContentLayer.Academic],
                var s when s.Contains("sport") || s.Contains("football") || s.Contains("soccer")
                    => [ContentLayer.Sports],
                var s when s.Contains("music") || s.Contains("afrobeat")
                    => [ContentLayer.Music],
                var s when s.Contains("film") || s.Contains("movie") || s.Contains("cinema") || s.Contains("tv")
                    => [ContentLayer.Film],
                var s when s.Contains("educat") || s.Contains("learn") || s.Contains("school") || s.Contains("university")
                    => [ContentLayer.Education, ContentLayer.Learning],
                var s when s.Contains("fashion") || s.Contains("style") || s.Contains("beauty")
                    => [ContentLayer.Fashion],
                var s when s.Contains("travel") || s.Contains("tourism")
                    => [ContentLayer.Travel],
                var s when s.Contains("faith") || s.Contains("religion") || s.Contains("spiritual")
                    => [ContentLayer.Faith],
                var s when s.Contains("philosophy") || s.Contains("ethics")
                    => [ContentLayer.Philosophy],
                var s when s.Contains("law") || s.Contains("legal")
                    => [ContentLayer.Law],
                var s when s.Contains("real estate") || s.Contains("property") || s.Contains("housing")
                    => [ContentLayer.RealEstate],
                var s when s.Contains("energy") || s.Contains("oil") || s.Contains("power") || s.Contains("gas")
                    => [ContentLayer.Energy],
                var s when s.Contains("agriculture") || s.Contains("farming") || s.Contains("food")
                    => [ContentLayer.Agriculture],
                var s when s.Contains("industry") || s.Contains("mining") || s.Contains("manufacturing")
                    => [ContentLayer.Industry],
                var s when s.Contains("career") || s.Contains("job") || s.Contains("work")
                    => [ContentLayer.Career],
                var s when s.Contains("art") || s.Contains("craft") || s.Contains("design")
                    => [ContentLayer.Art],
                var s when s.Contains("history")
                    => [ContentLayer.History],
                var s when s.Contains("gaming") || s.Contains("esport") || s.Contains("game")
                    => [ContentLayer.Gaming],
                var s when s.Contains("transport") || s.Contains("mobility") || s.Contains("logistic")
                    => [ContentLayer.Transportation],
                var s when s.Contains("book") || s.Contains("literature") || s.Contains("reading")
                    => [ContentLayer.Literature],
                var s when s.Contains("lifestyle")
                    => [ContentLayer.Lifestyle],
                _ => []
            };
            foreach (var l in mapped) layers.Add(l);
        }
        return [.. layers];
    }
}
