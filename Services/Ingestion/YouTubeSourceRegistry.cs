using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>A YouTube channel to ingest, identified by its handle (resolved to a channel id at runtime).</summary>
public sealed record YouTubeSource(string Id, string Name, string Handle, ContentLayer Layer, string[] Topics);

/// <summary>
/// YouTube channels ingested through their public Atom feed (no API key required). Handles are
/// resolved to channel ids once and cached in Mongo, so adding a channel is a one-line change here.
/// </summary>
public static class YouTubeSourceRegistry
{
    public static readonly IReadOnlyList<YouTubeSource> All =
    [
        new("yt-bbc-news",        "BBC News",               "@BBCNews",          ContentLayer.Policy,       ["News", "Global", "Politics"]),
        new("yt-bbc-sport",       "BBC Sport",              "@BBCSport",         ContentLayer.Sports,       ["Sports", "Football"]),
        new("yt-espn",            "ESPN",                   "@ESPN",             ContentLayer.Sports,       ["Sports", "Analysis"]),
        new("yt-ign",             "IGN",                    "@IGN",              ContentLayer.Gaming,       ["Gaming", "Reviews"]),
        new("yt-kinda-funny",     "Kinda Funny Games",      "@KindaFunnyGames",  ContentLayer.Gaming,       ["Gaming", "Culture"]),
        new("yt-the-verge",       "The Verge",              "@TheVerge",         ContentLayer.Ideas,        ["Technology", "Gadgets"]),
        new("yt-lex-fridman",     "Lex Fridman Podcast",    "@lexfridman",       ContentLayer.Ideas,        ["AI", "Science", "Technology"]),
        new("yt-diary-ceo",       "The Diary Of A CEO",     "@TheDiaryOfACEO",   ContentLayer.Career,       ["Business", "Leadership", "Career"]),
        new("yt-biggerpockets",   "BiggerPockets",          "@biggerpockets",    ContentLayer.RealEstate,   ["Real Estate", "Investing"]),
        new("yt-freightwaves",    "FreightWaves",           "@FreightWaves",     ContentLayer.Transportation, ["Freight", "Logistics", "Supply Chain"]),
        new("yt-history-hit",     "History Hit",            "@HistoryHit",       ContentLayer.History,      ["History", "Documentary"]),
    ];
}
