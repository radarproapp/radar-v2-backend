using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

public static class SourceRegistry
{
    public static readonly IReadOnlyList<FeedSource> All;

    // Built in a static constructor: it runs AFTER every static field initializer,
    // so the spread sources declared later in this class are initialized. (Building
    // All inline would run first in textual order and spread still-null arrays.)
    static SourceRegistry()
    {
        All = ((FeedSource[])[
            // Original layers
            .. PolicyTier1, .. PolicyTier2, .. BigIdeas, .. Academic,
            // Interest domains (first batch)
            .. RealEstateSources, .. LawSources, .. LiteratureSources,
            .. TransportationSources, .. GamingSources, .. ArtSources,
            .. HistorySources,
            // Expanded domain sources
            .. EnvironmentSources, .. HealthSources, .. ScienceSources,
            .. TechSources, .. PolicyNewsSources, .. SportsExpanded,
            .. MusicSources, .. FilmSources, .. EducationSources,
            .. FashionLifestyleSources, .. FaithPhilosophySources,
            // New sector layers
            .. EnergySources, .. FinanceSources, .. AgricultureSources,
            .. IndustrySources, .. CareerSources
        ]).AsReadOnly();
    }

    // ── Layer 1: Policy Intelligence — Tier 1 (Global Institutions) ──────────

    private static readonly FeedSource[] PolicyTier1 =
    [
        new() { Id = "worldbank-news",    Name = "World Bank",            Url = "https://feeds.worldbank.org/worldbank/all",                Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Development", "Economics", "Infrastructure"] },
        new() { Id = "imf-news",          Name = "IMF",                   Url = "https://www.imf.org/en/News/rss",                           Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Macroeconomics", "Finance", "Fiscal Policy"] },
        new() { Id = "oecd-news",         Name = "OECD",                  Url = "https://www.oecd.org/newsroom/rss.xml",                     Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Policy", "Innovation", "Education", "Tax"] },
        new() { Id = "un-news",           Name = "UN News",               Url = "https://news.un.org/feed/subscribe/en/news/all/rss.xml",    Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Development", "SDGs", "Humanitarian"] },
        new() { Id = "undp-news",         Name = "UNDP",                  Url = "https://www.undp.org/media/news/rss.xml",                   Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Development", "SDGs", "Governance"] },
        new() { Id = "unesco-news",       Name = "UNESCO",                Url = "https://www.unesco.org/en/articles?f=rss",                  Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Education", "Culture", "Science", "AI Ethics"] },
        new() { Id = "who-news",          Name = "WHO",                   Url = "https://www.who.int/rss-feeds/news-english.xml",            Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Health", "Pandemic", "UHC"] },
        new() { Id = "fao-news",          Name = "FAO",                   Url = "https://www.fao.org/news/rss-feed-detail/en/c/rss/",        Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Agriculture", "Food Security", "Climate"] },
        new() { Id = "ilo-news",          Name = "ILO",                   Url = "https://www.ilo.org/global/about-the-ilo/newsroom/news/rss/lang--en/index.htm", Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Labour", "Employment", "Skills"] },
        new() { Id = "wef-agenda",        Name = "World Economic Forum",  Url = "https://www.weforum.org/agenda/rss.xml",                    Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Global Risks", "Future of Work", "Technology"] },
        new() { Id = "unep-news",         Name = "UNEP",                  Url = "https://www.unep.org/news-and-stories/rss",                  Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Climate", "Environment", "Green Economy"] },
        new() { Id = "unctad-news",       Name = "UNCTAD",                Url = "https://unctad.org/rss.xml",                                Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Trade", "Digital Economy", "Development"] },
        new() { Id = "wto-news",          Name = "WTO",                   Url = "https://www.wto.org/english/news_e/rss_e/rss_e.xml",        Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Trade", "AfCFTA", "E-Commerce"] },
    ];

    // ── Layer 1: Policy Intelligence — Tier 2 (Africa-Centric Bodies) ────────

    private static readonly FeedSource[] PolicyTier2 =
    [
        new() { Id = "afdb-news",         Name = "African Development Bank",   Url = "https://www.afdb.org/en/documents/rss",                  Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 2, Topics = ["Africa", "Development", "Finance", "Infrastructure"] },
        new() { Id = "uneca-news",        Name = "UNECA",                      Url = "https://www.uneca.org/rss",                              Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 2, Topics = ["Africa", "Economics", "Development"] },
        new() { Id = "conversation-africa", Name = "The Conversation Africa",  Url = "https://theconversation.com/africa/articles.atom",       Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article,     Tier = 2, Topics = ["Africa", "Research", "Policy", "Education"], SourceType = FeedSourceType.Atom },
        new() { Id = "moibrahim",         Name = "Mo Ibrahim Foundation",      Url = "https://mo.ibrahim.foundation/rss",                      Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 2, Topics = ["Africa", "Governance", "Leadership"] },
        new() { Id = "african-arguments", Name = "African Arguments",          Url = "https://africanarguments.org/feed/",                     Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article,     Tier = 2, Topics = ["Africa", "Politics", "Society"] },
        new() { Id = "africa-country",    Name = "Africa Is a Country",        Url = "https://africasacountry.com/feed",                       Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article,     Tier = 2, Topics = ["Africa", "Politics", "Culture"] },
        new() { Id = "iss-africa",        Name = "ISS Africa",                 Url = "https://issafrica.org/rss",                              Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 2, Topics = ["Africa", "Security", "Governance"] },
        new() { Id = "smart-africa",      Name = "Smart Africa",               Url = "https://smartafrica.org/feed/",                          Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 2, Topics = ["Africa", "Digital", "Technology"] },
    ];

    // ── Layer 3: Big Ideas & Essays ────────────────────────────────────────────

    private static readonly FeedSource[] BigIdeas =
    [
        new() { Id = "project-syndicate",  Name = "Project Syndicate",      Url = "https://www.project-syndicate.org/rss",             Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Global Affairs", "Economics", "Policy"] },
        new() { Id = "aeon",               Name = "Aeon",                   Url = "https://aeon.co/feed.rss",                          Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Philosophy", "Science", "Psychology", "Big Ideas"] },
        new() { Id = "mit-tech-review",    Name = "MIT Technology Review",  Url = "https://www.technologyreview.com/feed/",            Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["AI", "Technology", "Science"] },
        new() { Id = "works-in-progress",  Name = "Works in Progress",      Url = "https://worksinprogress.co/feed/",                  Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Progress", "Science", "Policy", "Innovation"] },
        new() { Id = "noema",              Name = "Noema Magazine",         Url = "https://www.noemamag.com/feed/",                    Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Technology", "Society", "Philosophy"] },
        new() { Id = "ssir",               Name = "Stanford Social Innovation Review", Url = "https://ssir.org/site/rss",             Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Social Innovation", "Entrepreneurship", "Development"] },
        new() { Id = "foreign-affairs",    Name = "Foreign Affairs",        Url = "https://www.foreignaffairs.com/rss.xml",            Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Geopolitics", "International Relations", "Policy"] },
        new() { Id = "quanta",             Name = "Quanta Magazine",        Url = "https://www.quantamagazine.org/feed/",              Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Mathematics", "Physics", "Biology", "Computer Science"] },
        new() { Id = "our-world-data",     Name = "Our World in Data",      Url = "https://ourworldindata.org/atom.xml",               Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Data", "Development", "Health", "Environment"], SourceType = FeedSourceType.Atom },
        new() { Id = "openai-blog",        Name = "OpenAI Blog",            Url = "https://openai.com/blog/rss/",                      Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["AI", "Machine Learning", "Research"] },
        new() { Id = "anthropic-news",     Name = "Anthropic News",         Url = "https://www.anthropic.com/rss.xml",                 Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["AI", "Safety", "Research"] },
    ];

    // ── Real Estate ───────────────────────────────────────────────────────────

    private static readonly FeedSource[] RealEstateSources =
    [
        new() { Id = "housingwire",          Name = "HousingWire",              Url = "https://www.housingwire.com/feed/",                               Layer = ContentLayer.RealEstate, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Real Estate", "Housing", "Mortgage", "Markets"] },
        new() { Id = "africa-property-news", Name = "Africa Property News",      Url = "https://africapropertynews.com/feed/",                            Layer = ContentLayer.RealEstate, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Real Estate", "Property", "Development"] },
        new() { Id = "construction-review",  Name = "Construction Review Africa", Url = "https://constructionreviewonline.com/feed/",                      Layer = ContentLayer.RealEstate, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Infrastructure", "Construction", "Urban Development"] },
        new() { Id = "estate-intel",         Name = "Estate Intel",              Url = "https://estateintel.com/feed/",                                   Layer = ContentLayer.RealEstate, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Real Estate", "Investment", "Nigeria"] },
    ];

    // ── Law ───────────────────────────────────────────────────────────────────

    private static readonly FeedSource[] LawSources =
    [
        new() { Id = "aba-journal",          Name = "ABA Journal",              Url = "https://www.abajournal.com/feed/",                                Layer = ContentLayer.Law, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Law", "Legal", "Courts", "Regulation"] },
        new() { Id = "conversation-law",     Name = "The Conversation Law",     Url = "https://theconversation.com/global/topics/law-113/articles.atom", Layer = ContentLayer.Law, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Law", "Policy", "Governance", "Academic"], SourceType = FeedSourceType.Atom },
        new() { Id = "premium-times-ng",     Name = "Premium Times Nigeria",    Url = "https://www.premiumtimesng.com/feed/",                            Layer = ContentLayer.Law, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Nigeria", "Governance", "Legal"] },
        new() { Id = "africanlii",           Name = "African LII",              Url = "https://africanlii.org/feed",                                     Layer = ContentLayer.Law, DefaultContentType = ContentType.PolicyPaper, Tier = 2, Topics = ["Africa", "Law", "Courts", "Legislation"] },
    ];

    // ── Books & Literature ────────────────────────────────────────────────────

    private static readonly FeedSource[] LiteratureSources =
    [
        new() { Id = "literary-hub",         Name = "Literary Hub",             Url = "https://lithub.com/feed/",                                        Layer = ContentLayer.Literature, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Books", "Literature", "Writing", "Publishing"] },
        new() { Id = "nyrb",                 Name = "New York Review of Books", Url = "https://www.nybooks.com/feed/",                                   Layer = ContentLayer.Literature, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Books", "Literature", "Criticism", "Ideas"] },
        new() { Id = "paris-review",         Name = "The Paris Review",         Url = "https://www.theparisreview.org/feed/",                            Layer = ContentLayer.Literature, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Literature", "Fiction", "Poetry", "Interviews"] },
        new() { Id = "brittle-paper",        Name = "Brittle Paper",            Url = "https://brittlepaper.com/feed/",                                  Layer = ContentLayer.Literature, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Literature", "Books", "Writers"] },
        new() { Id = "jrb",                  Name = "Johannesburg Review of Books", Url = "https://johannesburgreviewofbooks.com/feed/",                  Layer = ContentLayer.Literature, DefaultContentType = ContentType.Essay, Tier = 2, Topics = ["Africa", "Books", "Literary Criticism"] },
    ];

    // ── Transportation ────────────────────────────────────────────────────────

    private static readonly FeedSource[] TransportationSources =
    [
        new() { Id = "intelligent-transport", Name = "Intelligent Transport",   Url = "https://www.intelligenttransport.com/feed/",                      Layer = ContentLayer.Transportation, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Transport", "Mobility", "Smart Cities", "Infrastructure"] },
        new() { Id = "smart-cities-transport", Name = "Smart Cities Dive",      Url = "https://www.smartcitiesdive.com/feeds/news/",                     Layer = ContentLayer.Transportation, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Smart Cities", "Urban Mobility", "Transport", "Logistics"] },
        new() { Id = "ttnews",               Name = "Transport Topics",         Url = "https://www.ttnews.com/rss",                                      Layer = ContentLayer.Transportation, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Trucking", "Freight", "Logistics", "Supply Chain"] },
        new() { Id = "businessday-transport", Name = "BusinessDay Transport",   Url = "https://businessday.ng/feed/",                                    Layer = ContentLayer.Transportation, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Nigeria", "Transport", "Infrastructure"] },
    ];

    // ── Gaming & Esports ──────────────────────────────────────────────────────

    private static readonly FeedSource[] GamingSources =
    [
        new() { Id = "game-developer",       Name = "Game Developer",           Url = "https://www.gamedeveloper.com/rss",                               Layer = ContentLayer.Gaming, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Game Dev", "Gaming Industry", "Design", "Technology"] },
        new() { Id = "gamesindustry",        Name = "GamesIndustry.biz",        Url = "https://www.gamesindustry.biz/rss/news",                          Layer = ContentLayer.Gaming, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Gaming Industry", "Business", "Publishers", "Platforms"] },
        new() { Id = "esports-insider",      Name = "Esports Insider",          Url = "https://esportsinsider.com/feed/",                                 Layer = ContentLayer.Gaming, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Esports", "Competitive Gaming", "Tournaments", "Teams"] },
        new() { Id = "dexerto",              Name = "Dexerto eSports",          Url = "https://www.dexerto.com/feed/",                                   Layer = ContentLayer.Gaming, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Esports", "Gaming", "Competitive", "News"] },
        new() { Id = "dot-esports",          Name = "Dot Esports",              Url = "https://dotesports.com/feed",                                     Layer = ContentLayer.Gaming, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Esports", "Gaming", "Guides", "News"] },
    ];

    // ── Art & Craft ───────────────────────────────────────────────────────────

    private static readonly FeedSource[] ArtSources =
    [
        new() { Id = "hyperallergic",        Name = "Hyperallergic",            Url = "https://hyperallergic.com/feed/",                                 Layer = ContentLayer.Art, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Art", "Design", "Culture", "Contemporary Art"] },
        new() { Id = "colossal",             Name = "Colossal",                 Url = "https://www.thisiscolossal.com/feed/",                            Layer = ContentLayer.Art, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Art", "Design", "Craft", "Visual Arts"] },
        new() { Id = "creative-boom",        Name = "Creative Boom",            Url = "https://www.creativeboom.com/feed/",                              Layer = ContentLayer.Art, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Creative", "Design", "Art", "Illustration"] },
        new() { Id = "art-africa-mag",       Name = "Art Africa Magazine",      Url = "https://artafricamagazine.org/feed/",                             Layer = ContentLayer.Art, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Art", "Contemporary Art", "Culture"] },
        new() { Id = "okayafrica-arts",      Name = "OkayAfrica Arts & Culture", Url = "https://www.okayafrica.com/feed/",                               Layer = ContentLayer.Art, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Culture", "Music", "Arts"] },
    ];

    // ── History ───────────────────────────────────────────────────────────────

    private static readonly FeedSource[] HistorySources =
    [
        new() { Id = "smithsonian-history",  Name = "Smithsonian History",      Url = "https://www.smithsonianmag.com/rss/history/",                     Layer = ContentLayer.History, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["History", "Archaeology", "Heritage", "Civilisation"] },
        new() { Id = "history-today",        Name = "History Today",            Url = "https://www.historytoday.com/feed",                               Layer = ContentLayer.History, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["History", "Research", "Social History", "Events"] },
        new() { Id = "archaeology-mag",      Name = "Archaeology Magazine",     Url = "https://www.archaeology.org/rss",                                 Layer = ContentLayer.History, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Archaeology", "History", "Civilisation", "Discovery"] },
    ];

    // ── Climate & Environment ─────────────────────────────────────────────────

    private static readonly FeedSource[] EnvironmentSources =
    [
        // Nigeria
        new() { Id = "climate-reporters-ng",    Name = "Climate Reporters Nigeria",  Url = "https://climatereporters.com/feed/",                    Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Climate", "Environment"] },
        new() { Id = "environews-nigeria",       Name = "EnviroNews Nigeria",          Url = "https://www.environewsnigeria.com/feed/",               Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Environment", "Climate"] },
        // Africa
        new() { Id = "mongabay-africa",          Name = "Mongabay Africa",             Url = "https://news.mongabay.com/africa/feed/",                Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Environment", "Forests", "Biodiversity"] },
        new() { Id = "africaclimatewire",        Name = "Africa Climate Wire",         Url = "https://africaclimatewire.org/feed/",                  Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Climate", "Policy"] },
        // International
        new() { Id = "carbon-brief",             Name = "Carbon Brief",                Url = "https://www.carbonbrief.org/feed/",                     Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Climate Science", "Climate Policy", "Data"] },
        new() { Id = "climate-change-news",      Name = "Climate Change News",         Url = "https://www.climatechangenews.com/feed/",               Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Climate Policy", "UNFCCC", "Energy Transition"] },
        new() { Id = "yale-climate",             Name = "Yale Climate Connections",    Url = "https://yaleclimateconnections.org/feed/",              Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Climate Science", "Solutions", "Community"] },
        new() { Id = "inside-climate",           Name = "Inside Climate News",         Url = "https://insideclimatenews.org/feed/",                   Layer = ContentLayer.Environment, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Climate", "Investigation", "Environment"] },
    ];

    // ── Health & Medicine (news sources) ──────────────────────────────────────

    private static readonly FeedSource[] HealthSources =
    [
        // Nigeria
        new() { Id = "nigeria-health-watch",     Name = "Nigeria Health Watch",        Url = "https://nigeriahealthwatch.com/feed/",                  Layer = ContentLayer.Medicine, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Health", "Policy", "Healthcare"] },
        new() { Id = "premiumtimes-health",      Name = "Premium Times Health",        Url = "https://www.premiumtimesng.com/health/feed/",           Layer = ContentLayer.Medicine, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Health", "Medicine"] },
        // Africa
        new() { Id = "bhekisisa",                Name = "Bhekisisa",                   Url = "https://bhekisisa.org/feed/",                           Layer = ContentLayer.Medicine, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["South Africa", "Africa", "Health", "Investigation"] },
        new() { Id = "health-e-news",            Name = "Health-e News",               Url = "https://health-e.org.za/feed/",                         Layer = ContentLayer.Medicine, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["South Africa", "Health", "HIV", "NHI"] },
        new() { Id = "africa-cdc",               Name = "Africa CDC",                  Url = "https://africacdc.org/feed/",                           Layer = ContentLayer.Medicine, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Africa", "Public Health", "Disease Control"] },
        // International
        new() { Id = "the-lancet",               Name = "The Lancet",                  Url = "https://www.thelancet.com/rssfeed/lancet_current.xml",  Layer = ContentLayer.Academic, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Medicine", "Research", "Clinical", "Global Health"] },
        new() { Id = "bmj",                      Name = "BMJ",                         Url = "https://www.bmj.com/rss/current.xml",                   Layer = ContentLayer.Academic, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Medicine", "Clinical Research", "Policy"] },
        new() { Id = "stat-news",                Name = "STAT News",                   Url = "https://www.statnews.com/feed/",                        Layer = ContentLayer.Medicine, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Biotech", "Medicine", "Pharma", "Research"] },
        new() { Id = "health-policy-watch",      Name = "Health Policy Watch",         Url = "https://healthpolicy-watch.news/feed/",                 Layer = ContentLayer.Medicine, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Global Health", "WHO", "Policy", "Access"] },
    ];

    // ── Science (news sources) ────────────────────────────────────────────────

    private static readonly FeedSource[] ScienceSources =
    [
        new() { Id = "nature-news",              Name = "Nature News",                 Url = "https://www.nature.com/nature.rss",                     Layer = ContentLayer.Science, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Science", "Research", "Multidisciplinary"] },
        new() { Id = "science-aaas",             Name = "Science (AAAS)",              Url = "https://www.science.org/rss/news_current.xml",          Layer = ContentLayer.Science, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Science", "Research", "Policy"] },
        new() { Id = "new-scientist",            Name = "New Scientist",               Url = "https://www.newscientist.com/feed/home/",               Layer = ContentLayer.Science, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Science", "Technology", "Space", "Health"] },
        new() { Id = "scientific-american",      Name = "Scientific American",         Url = "https://rss.sciam.com/ScientificAmerican-Global",       Layer = ContentLayer.Science, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Science", "Medicine", "Environment", "Mind"] },
        new() { Id = "live-science",             Name = "Live Science",                Url = "https://www.livescience.com/feeds/all",                  Layer = ContentLayer.Science, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Science", "Technology", "Health", "Space"] },
        new() { Id = "conversation-global",      Name = "The Conversation (Global)",   Url = "https://theconversation.com/articles.atom",             Layer = ContentLayer.Science, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Science", "Research", "Society"], SourceType = FeedSourceType.Atom },
    ];

    // ── Technology (expanded) ─────────────────────────────────────────────────

    private static readonly FeedSource[] TechSources =
    [
        // Nigeria
        new() { Id = "technext",                 Name = "Technext",                    Url = "https://technext24.com/feed/",                          Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Tech", "Startups", "Digital"] },
        new() { Id = "benjamin-dada",            Name = "Benjamin Dada",               Url = "https://www.benjamindada.com/feed/",                    Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Tech", "Startups", "Africa"] },
        // Africa
        new() { Id = "disrupt-africa",           Name = "Disrupt Africa",              Url = "https://disruptafrica.com/feed/",                       Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Startups", "Tech", "Investment"] },
        new() { Id = "connecting-africa",        Name = "Connecting Africa",           Url = "https://connectingafrica.com/feed/",                    Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Telecom", "Digital", "Tech"] },
        new() { Id = "itweb-africa",             Name = "ITWeb Africa",                Url = "https://itweb.africa/rss",                              Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Tech", "Enterprise", "Digital"] },
        new() { Id = "techcentral-za",           Name = "TechCentral South Africa",    Url = "https://techcentral.co.za/feed/",                       Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["South Africa", "Tech", "Fintech", "Mobile"] },
        // Global
        new() { Id = "the-verge",                Name = "The Verge",                   Url = "https://www.theverge.com/rss/index.xml",                Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Tech", "AI", "Gadgets", "Silicon Valley"] },
        new() { Id = "wired",                    Name = "WIRED",                       Url = "https://www.wired.com/feed/rss",                        Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Tech", "Culture", "Science", "AI"] },
        new() { Id = "ars-technica",             Name = "Ars Technica",                Url = "https://feeds.arstechnica.com/arstechnica/index",       Layer = ContentLayer.Ideas, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Tech", "Science", "Policy", "Security"] },
    ];

    // ── Policy & Business News (expanded) ────────────────────────────────────

    private static readonly FeedSource[] PolicyNewsSources =
    [
        // Nigeria
        new() { Id = "nairametrics",             Name = "Nairametrics",                Url = "https://nairametrics.com/feed/",                        Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Finance", "Economy", "Markets"] },
        new() { Id = "premiumtimes-politics",    Name = "Premium Times Politics",      Url = "https://www.premiumtimesng.com/politics/feed/",         Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Politics", "Governance"] },
        new() { Id = "thecable",                 Name = "TheCable",                    Url = "https://www.thecable.ng/feed",                          Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Politics", "Business", "Economy"] },
        // Africa
        new() { Id = "theafrica-report",         Name = "The Africa Report",           Url = "https://www.theafricareport.com/feed/",                 Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Politics", "Business", "Geopolitics"] },
        new() { Id = "african-business",         Name = "African Business",            Url = "https://african.business/feed",                         Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Business", "Investment", "Trade"] },
        new() { Id = "how-made-it-africa",       Name = "How We Made It In Africa",    Url = "https://www.howwemadeitinafrica.com/feed/",             Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Business", "Entrepreneurship", "Success"] },
        new() { Id = "cnbc-africa",              Name = "CNBC Africa",                 Url = "https://www.cnbcafrica.com/feed/",                      Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Markets", "Business", "Finance"] },
        new() { Id = "mail-guardian",            Name = "Mail & Guardian",             Url = "https://mg.co.za/feed/",                                Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["South Africa", "Africa", "Politics", "Society"] },
        // Global
        new() { Id = "reuters-world",            Name = "Reuters World",               Url = "https://feeds.reuters.com/Reuters/worldNews",           Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["World", "Geopolitics", "Politics", "Conflict"] },
        new() { Id = "reuters-business",         Name = "Reuters Business",            Url = "https://feeds.reuters.com/reuters/businessNews",        Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Business", "Finance", "Markets", "Economics"] },
        new() { Id = "politico",                 Name = "Politico",                    Url = "https://www.politico.com/rss/politicopicks.xml",        Layer = ContentLayer.Policy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["US Politics", "Policy", "Governance", "EU"] },
        new() { Id = "the-economist",            Name = "The Economist",               Url = "https://www.economist.com/latest/rss.xml",              Layer = ContentLayer.Policy, DefaultContentType = ContentType.Essay, Tier = 1, Topics = ["Economics", "Geopolitics", "Business", "Science"] },
        new() { Id = "chatham-house",            Name = "Chatham House",               Url = "https://www.chathamhouse.org/feed",                     Layer = ContentLayer.Policy, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Geopolitics", "Security", "International", "Africa"] },
    ];

    // ── Sports (expanded) ─────────────────────────────────────────────────────

    private static readonly FeedSource[] SportsExpanded =
    [
        // Nigeria
        new() { Id = "brila",                    Name = "Brila FM",                    Url = "https://brila.net/feed/",                               Layer = ContentLayer.Sports, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Football", "Sports", "Super Eagles"] },
        // Africa
        new() { Id = "kickoff",                  Name = "KickOff",                     Url = "https://www.kickoff.com/feeds/articles.php",            Layer = ContentLayer.Sports, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["South Africa", "Football", "PSL", "Africa"] },
        new() { Id = "sport-news-africa",        Name = "Sport News Africa",           Url = "https://sportnewsafrica.com/feed/",                     Layer = ContentLayer.Sports, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Sports", "Football", "Athletics"] },
        // Global
        new() { Id = "sky-sports",               Name = "Sky Sports",                  Url = "https://www.skysports.com/rss/12040",                   Layer = ContentLayer.Sports, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Football", "Premier League", "Sports"] },
        new() { Id = "bbc-sport",                Name = "BBC Sport",                   Url = "https://feeds.bbci.co.uk/sport/rss.xml",               Layer = ContentLayer.Sports, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Football", "Athletics", "Sports", "Africa"] },
        new() { Id = "goal-com",                 Name = "Goal",                        Url = "https://www.goal.com/feeds/en/news",                    Layer = ContentLayer.Sports, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Football", "Transfers", "Champions League"] },
    ];

    // ── Music (expanded + July 7 additions) ──────────────────────────────────

    private static readonly FeedSource[] MusicSources =
    [
        // Nigeria
        new() { Id = "notjustok",                Name = "NotJustOk",                   Url = "https://notjustok.com/feed/",                           Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Afrobeats", "Music", "Naija"] },
        new() { Id = "pulse-entertainment",      Name = "Pulse Entertainment",         Url = "https://www.pulse.ng/entertainment/feed/",              Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Entertainment", "Music", "Celebrities"] },
        new() { Id = "tooxclusive",              Name = "TooXclusive",                 Url = "https://tooxclusive.com/feed/",                         Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Music", "Afrobeats", "Reviews"] },
        new() { Id = "wetalksound",              Name = "WeTalkSound",                 Url = "https://www.wetalksound.co/feed/",                      Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Music", "Reviews", "Features"] },
        // Africa
        new() { Id = "music-in-africa",          Name = "Music In Africa",             Url = "https://www.musicinafrica.net/feed",                    Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Music", "Industry", "Artists"] },
        new() { Id = "pan-african-music",        Name = "Pan African Music",           Url = "https://pan-african-music.com/feed/",                   Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Music", "Culture", "World"] },
        // Global
        new() { Id = "billboard",                Name = "Billboard",                   Url = "https://www.billboard.com/feed/",                       Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Music", "Charts", "Industry", "Pop"] },
        new() { Id = "nme",                      Name = "NME",                         Url = "https://www.nme.com/feed",                              Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Music", "Rock", "Pop", "Indie", "Reviews"] },
        new() { Id = "stereogum",                Name = "Stereogum",                   Url = "https://www.stereogum.com/feed/",                       Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Music", "Indie", "Alternative", "Reviews"] },
        new() { Id = "consequence-music",        Name = "Consequence",                 Url = "https://consequence.net/feed/",                         Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Music", "Concerts", "Reviews", "Festivals"] },
        new() { Id = "music-business-worldwide", Name = "Music Business Worldwide",    Url = "https://www.musicbusinessworldwide.com/feed/",          Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Music Industry", "Streaming", "Rights", "Business"] },
        new() { Id = "hypebot",                  Name = "Hypebot",                     Url = "https://www.hypebot.com/feed",                          Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Music Tech", "Streaming", "Industry", "AI"] },
        new() { Id = "resident-advisor",         Name = "Resident Advisor",            Url = "https://ra.co/xml/news.xml",                            Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Electronic Music", "Dance", "DJs", "Clubs"] },
        new() { Id = "american-songwriter",      Name = "American Songwriter",         Url = "https://americansongwriter.com/feed/",                  Layer = ContentLayer.Music, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Songwriting", "Music", "Artists", "Interviews"] },
    ];

    // ── Film & TV (expanded + July 7 additions) ───────────────────────────────

    private static readonly FeedSource[] FilmSources =
    [
        // Africa
        new() { Id = "film-africa",              Name = "Film Africa",                 Url = "https://filmafrica.org.uk/feed/",                       Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Film", "Cinema", "Nollywood"] },
        // Global
        new() { Id = "variety",                  Name = "Variety",                     Url = "https://variety.com/feed/",                             Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Film", "TV", "Entertainment", "Box Office"] },
        new() { Id = "hollywood-reporter",       Name = "The Hollywood Reporter",      Url = "https://www.hollywoodreporter.com/feed/",               Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Film", "TV", "Entertainment", "Industry"] },
        new() { Id = "deadline",                 Name = "Deadline",                    Url = "https://deadline.com/feed/",                            Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Film", "TV", "Box Office", "Development"] },
        new() { Id = "indiewire",                Name = "IndieWire",                   Url = "https://www.indiewire.com/feed/",                       Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Indie Film", "Criticism", "TV", "Awards"] },
        new() { Id = "thewrap",                  Name = "TheWrap",                     Url = "https://www.thewrap.com/feed/",                         Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Film", "TV", "Streaming", "Business"] },
        new() { Id = "collider",                 Name = "Collider",                    Url = "https://collider.com/feed/",                            Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Film", "TV", "Reviews", "Trailers"] },
        new() { Id = "screenrant",               Name = "Screen Rant",                 Url = "https://screenrant.com/feed/",                          Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Film", "TV", "Comics", "Reviews"] },
        new() { Id = "rogerebert",               Name = "RogerEbert.com",              Url = "https://www.rogerebert.com/feed.xml",                   Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Film", "Criticism", "Reviews", "Essays"] },
        new() { Id = "tvline",                   Name = "TVLine",                      Url = "https://tvline.com/feed/",                              Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["TV", "Streaming", "Renewals", "Ratings"] },
        new() { Id = "screen-daily",             Name = "Screen Daily",                Url = "https://www.screendaily.com/rss/",                      Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["International Film", "Distribution", "Festivals", "Business"] },
        new() { Id = "empire-magazine",          Name = "Empire Magazine",             Url = "https://www.empireonline.com/movies/news/feed/",        Layer = ContentLayer.Film, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Film", "Reviews", "Blockbusters", "Interviews"] },
    ];

    // ── Education (expanded) ──────────────────────────────────────────────────

    private static readonly FeedSource[] EducationSources =
    [
        // Nigeria
        new() { Id = "educeleb",                 Name = "EduCeleb",                    Url = "https://educeleb.com/feed/",                            Layer = ContentLayer.Education, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Education", "Schools", "Scholarships"] },
        new() { Id = "myschoolgist",             Name = "MySchoolGist",                Url = "https://www.myschoolgist.com/feed/",                    Layer = ContentLayer.Education, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Education", "JAMB", "Admissions"] },
        // Global
        new() { Id = "times-higher-ed",          Name = "Times Higher Education",      Url = "https://www.timeshighereducation.com/feed.xml",         Layer = ContentLayer.Education, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Higher Education", "Universities", "Research", "Rankings"] },
        new() { Id = "inside-higher-ed",         Name = "Inside Higher Ed",            Url = "https://www.insidehighered.com/rss.xml",               Layer = ContentLayer.Education, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Higher Education", "Policy", "Research", "College"] },
        new() { Id = "university-world-news",    Name = "University World News",       Url = "https://www.universityworldnews.com/xml/rss.xml",       Layer = ContentLayer.Education, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Global HE", "Universities", "Policy", "Africa"] },
        new() { Id = "hechinger-report",         Name = "The Hechinger Report",        Url = "https://hechingerreport.org/feed/",                     Layer = ContentLayer.Education, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Education", "K-12", "Equity", "EdTech"] },
    ];

    // ── Fashion, Travel & Lifestyle ───────────────────────────────────────────

    private static readonly FeedSource[] FashionLifestyleSources =
    [
        // Nigeria
        new() { Id = "bellanaija-style",         Name = "BellaNaija Style",            Url = "https://www.bellanaijastyle.com/feed/",                 Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Fashion", "Style", "Beauty"] },
        new() { Id = "zikoko",                   Name = "Zikoko",                      Url = "https://www.zikoko.com/feed/",                          Layer = ContentLayer.Lifestyle, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Culture", "Lifestyle", "Humour"] },
        new() { Id = "glam-africa",              Name = "Glam Africa",                 Url = "https://glamafrica.com/feed/",                          Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Fashion", "Beauty", "Lifestyle"] },
        new() { Id = "exquisite-mag",            Name = "Exquisite Magazine",          Url = "https://exquisitemag.com/feed/",                        Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Fashion", "Beauty", "Lifestyle"] },
        // Africa
        new() { Id = "afrobella",                Name = "Afrobella",                   Url = "https://afrobella.com/feed/",                           Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Beauty", "Fashion", "Diaspora"] },
        new() { Id = "twyg",                     Name = "Twyg",                        Url = "https://twyg.co.za/feed/",                              Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["South Africa", "Fashion", "Sustainable", "Style"] },
        new() { Id = "design-indaba",            Name = "Design Indaba",               Url = "https://www.designindaba.com/feeds/news",               Layer = ContentLayer.Art, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Design", "Creativity", "Culture"] },
        new() { Id = "travel-news-africa",       Name = "Travel News Africa",          Url = "https://travelnews.africa/feed/",                       Layer = ContentLayer.Travel, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Travel", "Tourism", "Hospitality"] },
        // Global
        new() { Id = "vogue",                    Name = "Vogue",                       Url = "https://www.vogue.com/feed/rss",                        Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Fashion", "Beauty", "Culture", "Luxury"] },
        new() { Id = "gq",                       Name = "GQ",                          Url = "https://www.gq.com/feed/rss",                           Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Men's Fashion", "Culture", "Style", "Grooming"] },
        new() { Id = "harpers-bazaar",           Name = "Harper's BAZAAR",             Url = "https://www.harpersbazaar.com/rss/all.xml/",            Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Fashion", "Beauty", "Culture", "Luxury"] },
        new() { Id = "elle",                     Name = "ELLE",                        Url = "https://www.elle.com/rss/all.xml/",                     Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Fashion", "Beauty", "Culture", "Women"] },
        new() { Id = "hypebeast",                Name = "Hypebeast",                   Url = "https://hypebeast.com/feed",                            Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Streetwear", "Sneakers", "Culture", "Drops"] },
        new() { Id = "highsnobiety",             Name = "Highsnobiety",                Url = "https://www.highsnobiety.com/feed/",                    Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Streetwear", "Fashion", "Culture", "Music"] },
        new() { Id = "who-what-wear",            Name = "Who What Wear",               Url = "https://www.whowhatwear.com/feed",                      Layer = ContentLayer.Fashion, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Fashion", "Style", "Trends", "Shopping"] },
        new() { Id = "design-milk",              Name = "Design Milk",                 Url = "https://design-milk.com/feed/",                         Layer = ContentLayer.Art, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Design", "Architecture", "Art", "Lifestyle"] },
        new() { Id = "cnt-traveler",             Name = "Condé Nast Traveler",         Url = "https://www.cntraveler.com/feed/rss",                   Layer = ContentLayer.Travel, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Travel", "Destinations", "Luxury", "Tips"] },
        new() { Id = "natgeo-travel",            Name = "National Geographic Travel",  Url = "https://www.nationalgeographic.com/travel/article.rss", Layer = ContentLayer.Travel, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Travel", "Adventure", "Culture", "Nature"] },
    ];

    // ── Faith & Philosophy (expanded) ─────────────────────────────────────────

    private static readonly FeedSource[] FaithPhilosophySources =
    [
        // Nigeria
        new() { Id = "church-times-ng",          Name = "Church Times Nigeria",        Url = "https://churchtimesnigeria.net/feed/",                  Layer = ContentLayer.Faith, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Christianity", "Church", "Faith"] },
        new() { Id = "christianity-nigeria",     Name = "Christianity Nigeria",        Url = "https://christianitynigeria.com/feed/",                 Layer = ContentLayer.Faith, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Christianity", "Religion", "Faith"] },
        // Africa
        new() { Id = "the-elephant",             Name = "The Elephant",                Url = "https://www.theelephant.info/feed/",                    Layer = ContentLayer.Philosophy, DefaultContentType = ContentType.Essay, Tier = 2, Topics = ["Africa", "Society", "Ethics", "Policy"] },
        new() { Id = "agenzia-fides",            Name = "Agenzia Fides",               Url = "https://www.fides.org/en/news.rss",                     Layer = ContentLayer.Faith, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Catholic", "Africa", "Missions", "Church"] },
        // Global
        new() { Id = "religion-news-service",    Name = "Religion News Service",       Url = "https://religionnews.com/feed/",                        Layer = ContentLayer.Faith, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Religion", "Faith", "Spirituality", "Society"] },
        new() { Id = "philosophy-now",           Name = "Philosophy Now",              Url = "https://philosophynow.org/rss",                         Layer = ContentLayer.Philosophy, DefaultContentType = ContentType.Essay, Tier = 2, Topics = ["Philosophy", "Ethics", "Logic", "Ideas"] },
        new() { Id = "new-humanist",             Name = "New Humanist",                Url = "https://newhumanist.org.uk/feed",                       Layer = ContentLayer.Philosophy, DefaultContentType = ContentType.Essay, Tier = 2, Topics = ["Humanism", "Ethics", "Science", "Society"] },
        new() { Id = "conversation-philosophy",  Name = "The Conversation (Philosophy)", Url = "https://theconversation.com/global/topics/philosophy-114/articles.atom", Layer = ContentLayer.Philosophy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Philosophy", "Ethics", "Society"], SourceType = FeedSourceType.Atom },
    ];

    // ── Energy ────────────────────────────────────────────────────────────────

    private static readonly FeedSource[] EnergySources =
    [
        // Data & Policy
        new() { Id = "eia-today",                Name = "EIA Today in Energy",         Url = "https://www.eia.gov/rss/todayinenergy.xml",             Layer = ContentLayer.Energy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Energy Data", "Oil", "Gas", "Renewables", "US"] },
        // Africa/Nigeria
        new() { Id = "africa-energy-portal",     Name = "Africa Energy Portal",        Url = "https://energyportal.africa/feed/",                     Layer = ContentLayer.Energy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Africa", "Power", "Renewables", "Projects"] },
        // Global
        new() { Id = "cleantechnica",            Name = "CleanTechnica",               Url = "https://cleantechnica.com/feed/",                       Layer = ContentLayer.Energy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Renewables", "Solar", "EVs", "Cleantech"] },
        new() { Id = "utility-dive",             Name = "Utility Dive",                Url = "https://www.utilitydive.com/feeds/news",                Layer = ContentLayer.Energy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Power", "Utilities", "Grid", "Regulation"] },
        new() { Id = "energy-live-news",         Name = "Energy Live News",            Url = "https://www.energylivenews.com/feed/",                  Layer = ContentLayer.Energy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Energy", "Fossil Fuels", "Renewables", "Policy"] },
        new() { Id = "power-magazine",           Name = "Power Magazine",              Url = "https://www.powermag.com/feed/",                        Layer = ContentLayer.Energy, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Power Generation", "Coal", "Gas", "Nuclear", "Wind"] },
        new() { Id = "ogj",                      Name = "Oil & Gas Journal",           Url = "https://www.ogj.com/rss",                               Layer = ContentLayer.Energy, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Oil", "Gas", "Upstream", "Downstream", "Refining"] },
    ];

    // ── Finance (Banking, Fintech, Capital Markets, Personal Finance) ─────────

    private static readonly FeedSource[] FinanceSources =
    [
        // Fintech & Banking
        new() { Id = "finextra",                 Name = "Finextra",                    Url = "https://www.finextra.com/rss/rss.aspx",                 Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Fintech", "Banking", "Payments", "Regulation"] },
        // Nigeria Personal Finance
        new() { Id = "investors-king",           Name = "Investors King",              Url = "https://investorsking.com/feed/",                       Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Finance", "Investing", "Markets"] },
        new() { Id = "cowrywise-blog",           Name = "Cowrywise Blog",              Url = "https://cowrywise.com/blog/feed/",                      Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Investing", "Savings", "Wealth"] },
        new() { Id = "piggyvest-blog",           Name = "Piggyvest Money Tips",        Url = "https://blog.piggyvest.com/category/money-tips/feed/", Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Savings", "Budgeting", "Personal Finance"] },
        new() { Id = "money-matters-nimi",       Name = "Money Matters with Nimi",     Url = "https://www.moneymatterswithnimi.com/feed/",            Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Personal Finance", "Investing", "Wealth"] },
        new() { Id = "makemoney-ng",             Name = "MakeMoney.ng",                Url = "https://makemoney.ng/feed/",                            Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Personal Finance", "Digital Economy"] },
        new() { Id = "business-post-ng",         Name = "Business Post Nigeria",       Url = "https://businesspost.ng/feed/",                         Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Finance", "Banking", "Capital Markets"] },
        // Global Personal Finance
        new() { Id = "kiplinger",                Name = "Kiplinger",                   Url = "https://www.kiplinger.com/feed/all",                    Layer = ContentLayer.Finance, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Personal Finance", "Investing", "Retirement", "Tax"] },
    ];

    // ── Agriculture ───────────────────────────────────────────────────────────

    private static readonly FeedSource[] AgricultureSources =
    [
        // Nigeria
        new() { Id = "agronigeria",              Name = "AgroNigeria",                 Url = "https://agronigeria.ng/feed/",                          Layer = ContentLayer.Agriculture, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Agriculture", "Farming", "Value Chains"] },
        new() { Id = "fmafs-nigeria",            Name = "Federal Min. of Agriculture Nigeria", Url = "https://agriculture.gov.ng/feed/",             Layer = ContentLayer.Agriculture, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Nigeria", "Agriculture Policy", "Food Security"] },
        new() { Id = "afrimash-blog",            Name = "Afrimash Blog",               Url = "https://afrimash.com/feed/",                            Layer = ContentLayer.Agriculture, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Agribusiness", "Livestock", "Inputs"] },
        new() { Id = "nan-agriculture",          Name = "NAN Agriculture",             Url = "https://nannews.ng/category/agriculture/feed/",         Layer = ContentLayer.Agriculture, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Agriculture", "Government", "Farming"] },
        // Global
        new() { Id = "fao-newsroom",             Name = "FAO Newsroom",                Url = "https://www.fao.org/feeds/fao-newsroom-rss",           Layer = ContentLayer.Agriculture, DefaultContentType = ContentType.PolicyPaper, Tier = 1, Topics = ["Food Security", "Agriculture", "Trade", "Africa"] },
    ];

    // ── Industry (Manufacturing & Mining) ────────────────────────────────────

    private static readonly FeedSource[] IndustrySources =
    [
        // Africa
        new() { Id = "mining-weekly",            Name = "Mining Weekly",               Url = "https://www.miningweekly.com/page/home/feed",           Layer = ContentLayer.Industry, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Mining", "Africa", "Commodities", "Projects"] },
        new() { Id = "engineering-news",         Name = "Engineering News (Creamer)",  Url = "https://www.engineeringnews.co.za/rss/rss.xml",         Layer = ContentLayer.Industry, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Manufacturing", "Mining", "Engineering", "Africa"] },
        // Global
        new() { Id = "mining-com",               Name = "MINING.com",                  Url = "https://www.mining.com/feed/",                          Layer = ContentLayer.Industry, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Mining", "Critical Minerals", "Commodities", "Global"] },
        new() { Id = "international-mining",     Name = "International Mining",        Url = "https://im-mining.com/feed/",                           Layer = ContentLayer.Industry, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Mining", "Technology", "Projects", "Equipment"] },
    ];

    // ── Career & Professional Development ─────────────────────────────────────

    private static readonly FeedSource[] CareerSources =
    [
        // Nigeria/Africa
        new() { Id = "myjobmag",                 Name = "MyJobMag Career",             Url = "https://www.myjobmag.com/blog/feed/",                   Layer = ContentLayer.Career, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Nigeria", "Jobs", "Career Tips", "CV", "Interviews"] },
        // Global
        new() { Id = "bbc-worklife",             Name = "BBC Worklife",                Url = "https://feeds.bbci.co.uk/news/business/worklife/rss.xml", Layer = ContentLayer.Career, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Career", "Future of Work", "Skills", "Wellbeing"] },
        new() { Id = "the-muse",                 Name = "The Muse",                    Url = "https://www.themuse.com/advice/rss",                    Layer = ContentLayer.Career, DefaultContentType = ContentType.Article, Tier = 2, Topics = ["Career", "Job Search", "Workplace", "Growth"] },
        new() { Id = "hbr-careers",              Name = "Harvard Business Review",     Url = "https://feeds.hbr.org/harvardbusiness",                 Layer = ContentLayer.Career, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Leadership", "Management", "Career", "Strategy"] },
        new() { Id = "fast-company-work",        Name = "Fast Company",                Url = "https://www.fastcompany.com/latest/rss",                Layer = ContentLayer.Career, DefaultContentType = ContentType.Article, Tier = 1, Topics = ["Innovation", "Future of Work", "Design", "Tech"] },
    ];

    // ── Layer 2: Academic — OpenAlex API (/works with topics filter) ──────────
    // NOTE: OpenAlex deprecated `concepts.display_name` in Feb 2026 — use `topics.display_name`.

    private static readonly FeedSource[] Academic =
    [
        // Core STEM & AI
        new() { Id = "openalex-ai",           Name = "OpenAlex — AI & ML",            Url = "https://api.openalex.org/works?filter=topics.display_name:Artificial%20Intelligence,open_access.is_oa:true&sort=publication_date:desc&per_page=10", Layer = ContentLayer.Academic, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["AI", "Machine Learning"],                  SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-africa-econ",  Name = "OpenAlex — African Economics",  Url = "https://api.openalex.org/works?filter=topics.display_name:African%20economies,open_access.is_oa:true&sort=publication_date:desc&per_page=10",        Layer = ContentLayer.Academic, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Africa", "Economics", "Development"], SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-climate",      Name = "OpenAlex — Climate",            Url = "https://api.openalex.org/works?filter=topics.display_name:Climate%20Change,open_access.is_oa:true&sort=publication_date:desc&per_page=10",            Layer = ContentLayer.Academic, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Climate", "Environment"],              SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-health",       Name = "OpenAlex — Global Health",      Url = "https://api.openalex.org/works?filter=topics.display_name:Global%20Health,open_access.is_oa:true&sort=publication_date:desc&per_page=10",             Layer = ContentLayer.Academic, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Health", "Medicine", "Africa"],      SourceType = FeedSourceType.OpenAlexApi },
        // Sector domains
        new() { Id = "openalex-fintech",      Name = "OpenAlex — Fintech",            Url = "https://api.openalex.org/works?filter=topics.display_name:Financial%20Technology,open_access.is_oa:true&sort=publication_date:desc&per_page=10",      Layer = ContentLayer.Finance,   DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Finance", "Fintech", "Banking"],       SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-energy",       Name = "OpenAlex — Energy Transitions", Url = "https://api.openalex.org/works?filter=topics.display_name:Renewable%20Energy,open_access.is_oa:true&sort=publication_date:desc&per_page=10",          Layer = ContentLayer.Energy,    DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Energy", "Renewables", "Power"],       SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-agriculture",  Name = "OpenAlex — Agriculture Africa", Url = "https://api.openalex.org/works?filter=topics.display_name:African%20agriculture,open_access.is_oa:true&sort=publication_date:desc&per_page=10",       Layer = ContentLayer.Agriculture, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Agriculture", "Food Security"],     SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-urbanisation", Name = "OpenAlex — Urban Development",  Url = "https://api.openalex.org/works?filter=topics.display_name:Urban%20Development,open_access.is_oa:true&sort=publication_date:desc&per_page=10",         Layer = ContentLayer.RealEstate, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Urban", "Real Estate", "Housing"],    SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-education",    Name = "OpenAlex — Education Africa",   Url = "https://api.openalex.org/works?filter=topics.display_name:Education%20in%20Africa,open_access.is_oa:true&sort=publication_date:desc&per_page=10",     Layer = ContentLayer.Education, DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Education", "Africa", "Learning"],    SourceType = FeedSourceType.OpenAlexApi },
        new() { Id = "openalex-law-govern",   Name = "OpenAlex — Law & Governance",   Url = "https://api.openalex.org/works?filter=topics.display_name:Law%20and%20Governance,open_access.is_oa:true&sort=publication_date:desc&per_page=10",      Layer = ContentLayer.Law,       DefaultContentType = ContentType.ResearchPaper, Tier = 1, Topics = ["Law", "Governance", "Policy"],         SourceType = FeedSourceType.OpenAlexApi },
    ];
}
