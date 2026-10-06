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
        // Second batch (July 2026) — handles resolved and verified against the expected channel.
        new("yt-public-health",   "Public Health On Call",  "@PublicHealthOnCall", ContentLayer.Medicine,   ["Public Health", "Health Policy"]),
        new("yt-acquired",        "Acquired",               "@AcquiredFM",       ContentLayer.Finance,      ["Business", "Investing", "Technology"]),
        new("yt-planet-money",    "Planet Money",           "@PlanetMoney",      ContentLayer.Finance,      ["Economics", "Finance"]),
        new("yt-economist",       "The Economist",          "@TheEconomist",     ContentLayer.Policy,       ["Economy", "Politics", "Global"]),
        new("yt-men-in-blazers",  "Men in Blazers",         "@MenInBlazers",     ContentLayer.Sports,       ["Football", "Sports"]),
        new("yt-athletic-fc",     "The Athletic FC",        "@TheAthleticFC",    ContentLayer.Sports,       ["Football", "Sports"]),
        new("yt-song-exploder",   "Song Exploder",          "@SongExploder",     ContentLayer.Music,        ["Music", "Production"]),
        new("yt-indiewire",       "IndieWire",              "@IndieWire",        ContentLayer.Film,         ["Film", "TV"]),
        new("yt-edsurge",         "EdSurge",                "@EdSurge",          ContentLayer.Education,    ["Education", "EdTech"]),
        new("yt-cntraveler",      "Condé Nast Traveler",    "@cntraveler",       ContentLayer.Travel,       ["Travel", "Tourism"]),
        new("yt-zero-to-travel",  "Zero To Travel",         "@ZeroToTravel",     ContentLayer.Travel,       ["Travel", "Lifestyle"]),
        new("yt-on-being",        "On Being",               "@onbeing",          ContentLayer.Faith,        ["Faith", "Philosophy", "Society"]),
        new("yt-housingwire",     "HousingWire",            "@HousingWire",      ContentLayer.RealEstate,   ["Real Estate", "Housing", "Mortgage"]),
        new("yt-legal-talk",      "Legal Talk Network",     "@LegalTalkNetwork", ContentLayer.Law,          ["Law", "Legal"]),
        new("yt-esports-insider", "Esports Insider",        "@EsportsInsider",   ContentLayer.Gaming,       ["Esports", "Gaming"]),
        new("yt-creative-pep",    "Creative Pep Talk",      "@creativepeptalk",  ContentLayer.Art,          ["Creative", "Design", "Art"]),
        new("yt-new-yorker",      "The New Yorker",         "@newyorker",        ContentLayer.Literature,   ["Culture", "Fiction", "Ideas"]),
        new("yt-bbc-world",       "BBC World Service",      "@BBCWorldService",  ContentLayer.Policy,       ["News", "Global", "Politics"]),
        new("yt-nyt",             "The New York Times",     "@nytimes",          ContentLayer.Policy,       ["News", "Global", "Politics"]),
        new("yt-bloomberg-tv",    "Bloomberg Television",   "@markets",          ContentLayer.Finance,      ["Markets", "Finance", "Business"]),
        new("yt-afrobeats",       "Afrobeats Intelligence", "@afrobeatsintelligence", ContentLayer.Music,   ["Afrobeats", "Music", "Africa"]),
        new("yt-scifri",          "Science Friday",         "@scifri",           ContentLayer.Science,      ["Science", "Research"]),
        new("yt-education-next",  "Education Next",         "@EducationNext",    ContentLayer.Education,    ["Education", "Policy"]),
    ];
}
