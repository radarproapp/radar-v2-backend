using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

public static class LibraryRegistry
{
    // All must be built in a static constructor, not a field initializer: field initializers
    // run in declaration order, and this one is declared before the arrays it spreads (Climate,
    // Health, ...) further down the file — at field-initializer time those are still null,
    // throwing ArgumentNullException. Same bug class already fixed once in SourceRegistry.
    public static readonly IReadOnlyList<LibraryDocument> All;

    static LibraryRegistry()
    {
        All = ((LibraryDocument[])[.. Climate, .. Health, .. Science, .. Technology,
            .. BusinessFinance, .. Politics, .. Sports, .. Music, .. FilmTv,
            .. Education, .. Fashion, .. Lifestyle, .. FaithReligion,
            .. Philosophy, .. Environment, .. Travel, .. Medicine,
            .. RealEstate, .. Law, .. Literature, .. History, .. GamingEsports, .. ArtCraft
        ]).AsReadOnly();
    }

    // ── Climate ───────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Climate =
    [
        new() { Title = "Climate Change 2023: Synthesis Report (AR6)", Year = 2023, Publisher = "IPCC", DocumentType = "Assessment Report", Region = "Global", Category = "Climate", Subtopic = "Climate Science", Url = "https://www.ipcc.ch/report/ar6/syr/downloads/report/IPCC_AR6_SYR_FullVolume.pdf" },
        new() { Title = "Climate Change 2023: Summary for Policymakers", Year = 2023, Publisher = "IPCC", DocumentType = "Summary Report", Region = "Global", Category = "Climate", Subtopic = "Climate Policy", Url = "https://www.ipcc.ch/report/ar6/syr/downloads/report/IPCC_AR6_SYR_SPM.pdf" },
        new() { Title = "Emissions Gap Report 2025: Off Target", Year = 2025, Publisher = "UNEP", DocumentType = "Flagship Report", Region = "Global", Category = "Climate", Subtopic = "Climate Policy", Url = "https://www.unep.org/resources/emissions-gap-report" },
        new() { Title = "Adaptation Gap Report 2025: Running on Empty", Year = 2025, Publisher = "UNEP", DocumentType = "Flagship Report", Region = "Global", Category = "Climate", Subtopic = "Climate Adaptation", Url = "https://www.unep.org/resources/adaptation-gap-report" },
        new() { Title = "State of the Global Climate 2025", Year = 2026, Publisher = "WMO", DocumentType = "Annual Assessment Report", Region = "Global", Category = "Climate", Subtopic = "Climate Monitoring", Url = "https://wmo.int/publication-series/state-of-global-climate/state-of-global-climate-2025" },
        new() { Title = "Global Environment Outlook 7", Year = 2025, Publisher = "UNEP", DocumentType = "Global Assessment Report", Region = "Global", Category = "Climate", Subtopic = "Environment & Climate", Url = "https://www.unep.org/publications-data" },
        new() { Title = "Frontiers 2025: The Weight of Time", Year = 2025, Publisher = "UNEP", DocumentType = "Emerging Issues Report", Region = "Global", Category = "Climate", Subtopic = "Emerging Climate Risks", Url = "https://www.unep.org/publications-data" },
        new() { Title = "Climate Change 2022: Mitigation of Climate Change (WGIII)", Year = 2022, Publisher = "IPCC", DocumentType = "Assessment Report", Region = "Global", Category = "Climate", Subtopic = "Climate Mitigation", Url = "https://www.ipcc.ch/report/ar6/wg3/downloads/report/IPCC_AR6_WGIII_FullReport.pdf" },
        new() { Title = "Climate Change 2022: Impacts, Adaptation and Vulnerability (WGII)", Year = 2022, Publisher = "IPCC", DocumentType = "Assessment Report", Region = "Global", Category = "Climate", Subtopic = "Climate Adaptation", Url = "https://www.ipcc.ch/report/ar6/wg2/downloads/report/IPCC_AR6_WGII_FullReport.pdf" },
        new() { Title = "Climate Change 2021: The Physical Science Basis (WGI)", Year = 2021, Publisher = "IPCC", DocumentType = "Assessment Report", Region = "Global", Category = "Climate", Subtopic = "Climate Science", Url = "https://www.ipcc.ch/report/ar6/wg1/downloads/report/IPCC_AR6_WGI_FullReport.pdf" },
    ];

    // ── Health ────────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Health =
    [
        new() { Title = "World Health Statistics 2026: Monitoring Health for the SDGs", Year = 2026, Publisher = "WHO", DocumentType = "Annual Global Report", Region = "Global", Category = "Health", Subtopic = "Global Health Statistics", Url = "https://www.who.int/publications/i/item/9789240122482" },
        new() { Title = "World Health Statistics 2025: Monitoring Health for the SDGs", Year = 2025, Publisher = "WHO", DocumentType = "Annual Global Report", Region = "Global", Category = "Health", Subtopic = "Global Health Statistics", Url = "https://www.who.int/publications/i/item/9789240110496" },
        new() { Title = "World Health Statistics 2024: Monitoring Health for the SDGs", Year = 2024, Publisher = "WHO", DocumentType = "Annual Global Report", Region = "Global", Category = "Health", Subtopic = "Global Health Statistics", Url = "https://www.who.int/data/gho/publications/world-health-statistics" },
        new() { Title = "World Malaria Report 2025", Year = 2025, Publisher = "WHO", DocumentType = "Flagship Report", Region = "Global", Category = "Health", Subtopic = "Malaria", Url = "https://www.who.int/teams/global-malaria-programme/reports/world-malaria-report" },
        new() { Title = "Global Tuberculosis Report 2025", Year = 2025, Publisher = "WHO", DocumentType = "Flagship Report", Region = "Global", Category = "Health", Subtopic = "Tuberculosis", Url = "https://www.who.int/teams/global-tuberculosis-programme/tb-reports" },
        new() { Title = "Global Report on Neglected Tropical Diseases 2025", Year = 2025, Publisher = "WHO", DocumentType = "Global Report", Region = "Global", Category = "Health", Subtopic = "Neglected Tropical Diseases", Url = "https://www.who.int/teams/control-of-neglected-tropical-diseases/resources/reports" },
        new() { Title = "Global Hepatitis Report 2024", Year = 2024, Publisher = "WHO", DocumentType = "Global Report", Region = "Global", Category = "Health", Subtopic = "Viral Hepatitis", Url = "https://www.who.int/publications" },
        new() { Title = "Global Status Report on Physical Activity", Year = 2024, Publisher = "WHO", DocumentType = "Global Report", Region = "Global", Category = "Health", Subtopic = "Physical Activity", Url = "https://www.who.int/publications" },
        new() { Title = "Global Status Report on Alcohol and Health", Year = 2024, Publisher = "WHO", DocumentType = "Global Report", Region = "Global", Category = "Health", Subtopic = "Alcohol & Public Health", Url = "https://www.who.int/publications" },
        new() { Title = "Global Strategy for Women's, Children's and Adolescents' Health: Progress Report", Year = 2025, Publisher = "WHO", DocumentType = "Progress Report", Region = "Global", Category = "Health", Subtopic = "Maternal & Child Health", Url = "https://www.who.int/publications" },
        new() { Title = "Global Health Observatory Publications (Annual Collection)", Year = 2025, Publisher = "WHO", DocumentType = "Statistical Reports", Region = "Global", Category = "Health", Subtopic = "Health Data & Indicators", Url = "https://www.who.int/data/gho/publications/world-health-statistics" },
    ];

    // ── Science ───────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Science =
    [
        new() { Title = "UNESCO Science Report 2021: The Race Against Time for Smarter Development", Year = 2021, Publisher = "UNESCO", DocumentType = "Flagship Science Report", Region = "Global", Category = "Science", Subtopic = "Science Policy", Url = "https://unesdoc.unesco.org/ark:/48223/pf0000377433" },
        new() { Title = "Global Innovation Index 2025", Year = 2025, Publisher = "WIPO", DocumentType = "Annual Report", Region = "Global", Category = "Science", Subtopic = "Science & Innovation", Url = "https://www.wipo.int/global_innovation_index" },
        new() { Title = "Nature Index Annual Tables 2025", Year = 2025, Publisher = "Nature Portfolio", DocumentType = "Research Rankings", Region = "Global", Category = "Science", Subtopic = "Scientific Output", Url = "https://www.nature.com/nature-index/" },
        new() { Title = "Explainable AI (XAI) from a User Perspective: A Synthesis of Prior Literature", Year = 2022, Publisher = "arXiv", DocumentType = "Systematic Literature Review", Region = "Global", Category = "Science", Subtopic = "Artificial Intelligence", Url = "https://arxiv.org/abs/2211.15343" },
        new() { Title = "Annual Review of Earth and Planetary Sciences (Latest Volumes)", Year = 2025, Publisher = "Annual Reviews", DocumentType = "Review Journal", Region = "Global", Category = "Science", Subtopic = "Earth Science", Url = "https://www.annualreviews.org/journal/earth" },
        new() { Title = "PNAS – Proceedings of the National Academy of Sciences (Latest Research)", Year = 2025, Publisher = "National Academy of Sciences", DocumentType = "Peer-reviewed Journal", Region = "Global", Category = "Science", Subtopic = "Multidisciplinary Science", Url = "https://www.pnas.org" },
        new() { Title = "Science (AAAS) – Research Articles Collection 2025–2026", Year = 2025, Publisher = "AAAS", DocumentType = "Peer-reviewed Journal", Region = "Global", Category = "Science", Subtopic = "Multidisciplinary Science", Url = "https://www.science.org/journal/science" },
    ];

    // ── Technology ────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Technology =
    [
        new() { Title = "Artificial Intelligence Index Report 2026", Year = 2026, Publisher = "Stanford HAI", DocumentType = "Annual AI Report", Region = "Global", Category = "Technology", Subtopic = "Artificial Intelligence", Url = "https://hai.stanford.edu/ai-index/2026-ai-index-report" },
        new() { Title = "Artificial Intelligence Index Report 2025", Year = 2025, Publisher = "Stanford HAI", DocumentType = "Annual AI Report", Region = "Global", Category = "Technology", Subtopic = "Artificial Intelligence", Url = "https://hai.stanford.edu/ai-index/2025-ai-index-report" },
        new() { Title = "Artificial Intelligence Index Report 2024", Year = 2024, Publisher = "Stanford HAI", DocumentType = "Annual AI Report", Region = "Global", Category = "Technology", Subtopic = "Artificial Intelligence", Url = "https://hai.stanford.edu/ai-index/2024-ai-index-report" },
        new() { Title = "OECD Digital Economy Outlook 2024 (Volume 1): Embracing the Technology Frontier", Year = 2024, Publisher = "OECD", DocumentType = "Flagship Report", Region = "Global", Category = "Technology", Subtopic = "Digital Economy", Url = "https://www.oecd.org/en/publications/oecd-digital-economy-outlook-2024-volume-1_a1689dc5-en.html" },
        new() { Title = "The OECD.AI Index 2026", Year = 2026, Publisher = "OECD", DocumentType = "AI Measurement Report", Region = "Global", Category = "Technology", Subtopic = "AI Governance", Url = "https://www.oecd.org/en/publications/2026/02/oecd-ai-observatory-index_8f5fa0f2.html" },
        new() { Title = "GPT-4 Technical Report", Year = 2023, Publisher = "OpenAI", DocumentType = "Technical Report", Region = "Global", Category = "Technology", Subtopic = "Large Language Models", Url = "https://arxiv.org/abs/2303.08774" },
        new() { Title = "NIST AI Risk Management Framework 1.0", Year = 2023, Publisher = "NIST", DocumentType = "Framework", Region = "United States", Category = "Technology", Subtopic = "AI Risk Management", Url = "https://www.nist.gov/itl/ai-risk-management-framework" },
        new() { Title = "NIST Generative AI Profile", Year = 2024, Publisher = "NIST", DocumentType = "Implementation Guide", Region = "United States", Category = "Technology", Subtopic = "Generative AI", Url = "https://www.nist.gov/itl/ai-risk-management-framework/generative-ai-profile" },
        new() { Title = "Global Cybersecurity Outlook 2025", Year = 2025, Publisher = "World Economic Forum", DocumentType = "Annual Report", Region = "Global", Category = "Technology", Subtopic = "Cybersecurity", Url = "https://www.weforum.org/publications/global-cybersecurity-outlook-2025/" },
        new() { Title = "Measuring Digital Development: Facts and Figures 2025", Year = 2025, Publisher = "ITU", DocumentType = "Statistical Report", Region = "Global", Category = "Technology", Subtopic = "ICT Development", Url = "https://www.itu.int/hub/publications/" },
        new() { Title = "State of Open Source 2025", Year = 2025, Publisher = "Linux Foundation", DocumentType = "Industry Report", Region = "Global", Category = "Technology", Subtopic = "Open Source Software", Url = "https://www.linuxfoundation.org/research" },
    ];

    // ── Business & Finance ────────────────────────────────────────────────────

    private static readonly LibraryDocument[] BusinessFinance =
    [
        new() { Title = "World Economic Outlook: A Critical Juncture amid Policy Shifts (April 2025)", Year = 2025, Publisher = "IMF", DocumentType = "Flagship Report", Region = "Global", Category = "Business & Finance", Subtopic = "Global Economy", Url = "https://www.imf.org/en/Publications/WEO/Issues/2025/04/22/world-economic-outlook-april-2025" },
        new() { Title = "World Economic Outlook Update: Tenuous Resilience amid Persistent Uncertainty (July 2025)", Year = 2025, Publisher = "IMF", DocumentType = "Economic Outlook Update", Region = "Global", Category = "Business & Finance", Subtopic = "Macroeconomics", Url = "https://www.imf.org/en/Publications/WEO/Issues/2025/07/29/world-economic-outlook-update-july-2025" },
        new() { Title = "Global Financial Stability Report: Enhancing Resilience amid Uncertainty", Year = 2025, Publisher = "IMF", DocumentType = "Flagship Report", Region = "Global", Category = "Business & Finance", Subtopic = "Financial Stability", Url = "https://www.imf.org/en/Publications/GFSR/Issues/2025/04/22/global-financial-stability-report-april-2025" },
        new() { Title = "OECD Economic Outlook, Volume 2025 Issue 1", Year = 2025, Publisher = "OECD", DocumentType = "Economic Outlook", Region = "Global", Category = "Business & Finance", Subtopic = "Economic Growth", Url = "https://www.oecd.org/en/publications/oecd-economic-outlook.html" },
        new() { Title = "Global Economic Prospects 2025", Year = 2025, Publisher = "World Bank", DocumentType = "Flagship Report", Region = "Global", Category = "Business & Finance", Subtopic = "Global Economy", Url = "https://www.worldbank.org/en/publication/global-economic-prospects" },
        new() { Title = "Business Ready (B-READY) Report 2025", Year = 2025, Publisher = "World Bank", DocumentType = "Business Environment Report", Region = "Global", Category = "Business & Finance", Subtopic = "Private Sector Development", Url = "https://www.worldbank.org/en/businessready" },
        new() { Title = "Africa's Pulse 2025", Year = 2025, Publisher = "World Bank", DocumentType = "Regional Economic Report", Region = "Africa", Category = "Business & Finance", Subtopic = "African Economies", Url = "https://www.worldbank.org/en/publication/africas-pulse", IsAfrica = true },
        new() { Title = "African Economic Outlook 2025", Year = 2025, Publisher = "African Development Bank", DocumentType = "Flagship Report", Region = "Africa", Category = "Business & Finance", Subtopic = "Economic Development", Url = "https://www.afdb.org/en/documents/african-economic-outlook", IsAfrica = true },
        new() { Title = "Trade and Development Report 2025", Year = 2025, Publisher = "UNCTAD", DocumentType = "Flagship Report", Region = "Global", Category = "Business & Finance", Subtopic = "International Trade & Finance", Url = "https://unctad.org/publications-search?series=Trade%20and%20Development%20Report" },
    ];

    // ── Politics ──────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Politics =
    [
        new() { Title = "Freedom in the World 2026: The Growing Shadow of Autocracy", Year = 2026, Publisher = "Freedom House", DocumentType = "Annual Global Democracy Report", Region = "Global", Category = "Politics", Subtopic = "Democracy & Civil Liberties", Url = "https://freedomhouse.org/report/freedom-world" },
        new() { Title = "Freedom in the World 2025: The Uphill Battle to Safeguard Rights", Year = 2025, Publisher = "Freedom House", DocumentType = "Annual Global Democracy Report", Region = "Global", Category = "Politics", Subtopic = "Political Rights", Url = "https://freedomhouse.org/report/freedom-world" },
        new() { Title = "Freedom on the Net 2025", Year = 2025, Publisher = "Freedom House", DocumentType = "Annual Report", Region = "Global", Category = "Politics", Subtopic = "Internet Freedom & Digital Rights", Url = "https://freedomhouse.org/report/freedom-net" },
        new() { Title = "States of Fragility 2025", Year = 2025, Publisher = "OECD", DocumentType = "Flagship Report", Region = "Global", Category = "Politics", Subtopic = "Governance, Conflict & State Fragility", Url = "https://www.oecd.org/en/publications/2025/02/states-of-fragility-2025_c9080496/full-report.html" },
        new() { Title = "Global Risks Report 2025", Year = 2025, Publisher = "World Economic Forum", DocumentType = "Annual Risk Assessment", Region = "Global", Category = "Politics", Subtopic = "Geopolitics & Global Governance", Url = "https://www.weforum.org/publications/global-risks-report-2025/" },
        new() { Title = "Democracy Report 2025", Year = 2025, Publisher = "V-Dem Institute", DocumentType = "Annual Democracy Report", Region = "Global", Category = "Politics", Subtopic = "Democratic Governance", Url = "https://www.v-dem.net/publications/democracy-reports/" },
        new() { Title = "Human Development Report 2025", Year = 2025, Publisher = "UNDP", DocumentType = "Flagship Report", Region = "Global", Category = "Politics", Subtopic = "Governance & Human Development", Url = "https://hdr.undp.org/" },
        new() { Title = "World Migration Report 2026", Year = 2026, Publisher = "IOM", DocumentType = "Flagship Report", Region = "Global", Category = "Politics", Subtopic = "Migration Policy", Url = "https://worldmigrationreport.iom.int/" },
        new() { Title = "Corruption Perceptions Index 2025", Year = 2025, Publisher = "Transparency International", DocumentType = "Annual Index Report", Region = "Global", Category = "Politics", Subtopic = "Corruption & Accountability", Url = "https://www.transparency.org/en/cpi" },
        new() { Title = "Global Peace Index 2025", Year = 2025, Publisher = "Institute for Economics & Peace", DocumentType = "Annual Index Report", Region = "Global", Category = "Politics", Subtopic = "Peace & Political Stability", Url = "https://www.visionofhumanity.org/global-peace-index/" },
        new() { Title = "Ibrahim Index of African Governance (IIAG)", Year = 2025, Publisher = "Mo Ibrahim Foundation", DocumentType = "Governance Index", Region = "Africa", Category = "Politics", Subtopic = "African Governance & Public Leadership", Url = "https://iiag.online/", IsAfrica = true },
    ];

    // ── Sports ────────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Sports =
    [
        new() { Title = "FIFA Annual Report 2025", Year = 2025, Publisher = "FIFA", DocumentType = "Annual Report", Region = "Global", Category = "Sports", Subtopic = "Football Governance", Url = "https://inside.fifa.com/official-documents/annual-report/2025" },
        new() { Title = "FIFA Strategic Objectives for the Global Game: 2023–2027", Year = 2023, Publisher = "FIFA", DocumentType = "Strategic Framework", Region = "Global", Category = "Sports", Subtopic = "Sports Governance", Url = "https://inside.fifa.com/en/official-documents" },
        new() { Title = "FIFA Women's World Cup 2023™ Global Engagement & Audience Report", Year = 2024, Publisher = "FIFA", DocumentType = "Audience Research Report", Region = "Global", Category = "Sports", Subtopic = "Women's Football", Url = "https://inside.fifa.com/en/official-documents" },
        new() { Title = "FIFA Benchmarking Report 4th Edition: Setting the Pace", Year = 2025, Publisher = "FIFA", DocumentType = "Benchmarking Report", Region = "Global", Category = "Sports", Subtopic = "Women's Football Development", Url = "https://inside.fifa.com/en/official-documents" },
        new() { Title = "FIFA Transfer Report 2025", Year = 2025, Publisher = "FIFA", DocumentType = "Industry Report", Region = "Global", Category = "Sports", Subtopic = "Player Transfers", Url = "https://inside.fifa.com/en/official-documents" },
        new() { Title = "FIFA World Cup 26™ Sustainability & Human Rights Strategy", Year = 2025, Publisher = "FIFA", DocumentType = "Strategy Report", Region = "Global", Category = "Sports", Subtopic = "Human Rights in Sport", Url = "https://inside.fifa.com/en/official-documents" },
        new() { Title = "Olympic Agenda 2020+5", Year = 2021, Publisher = "IOC", DocumentType = "Strategic Roadmap", Region = "Global", Category = "Sports", Subtopic = "Olympic Governance", Url = "https://olympics.com/ioc/olympic-agenda-2020-plus-5" },
        new() { Title = "World Anti-Doping Code 2021 (International Standard)", Year = 2021, Publisher = "WADA", DocumentType = "Regulatory Framework", Region = "Global", Category = "Sports", Subtopic = "Anti-Doping", Url = "https://www.wada-ama.org/en/resources/world-anti-doping-program/world-anti-doping-code" },
    ];

    // ── Music ─────────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Music =
    [
        new() { Title = "Global Music Report 2026: State of the Industry", Year = 2026, Publisher = "IFPI", DocumentType = "Annual Industry Report", Region = "Global", Category = "Music", Subtopic = "Recorded Music Industry", Url = "https://www.ifpi.org/resources/" },
        new() { Title = "Global Music Report 2025: State of the Industry", Year = 2025, Publisher = "IFPI", DocumentType = "Annual Industry Report", Region = "Global", Category = "Music", Subtopic = "Recorded Music Industry", Url = "https://www.ifpi.org/resources/" },
        new() { Title = "Engaging with Music 2023", Year = 2023, Publisher = "IFPI", DocumentType = "Consumer Research Report", Region = "Global", Category = "Music", Subtopic = "Music Consumption", Url = "https://www.ifpi.org/resources/" },
        new() { Title = "Global Music Report 2024: State of the Industry", Year = 2024, Publisher = "IFPI", DocumentType = "Annual Industry Report", Region = "Global", Category = "Music", Subtopic = "Recorded Music", Url = "https://www.ifpi.org/resources/" },
        new() { Title = "Reshaping Policies for Creativity: Culture as a Global Public Good", Year = 2022, Publisher = "UNESCO", DocumentType = "Global Monitoring Report", Region = "Global", Category = "Music", Subtopic = "Music & Cultural Industries", Url = "https://unesdoc.unesco.org/" },
        new() { Title = "Global Collection Report 2025", Year = 2025, Publisher = "CISAC", DocumentType = "Annual Report", Region = "Global", Category = "Music", Subtopic = "Music Royalties", Url = "https://www.cisac.org/services/reports-and-surveys" },
        new() { Title = "World Intellectual Property Indicators 2025", Year = 2025, Publisher = "WIPO", DocumentType = "Statistical Report", Region = "Global", Category = "Music", Subtopic = "Copyright & Creative Industries", Url = "https://www.wipo.int/publications/" },
    ];

    // ── Film & TV ─────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] FilmTv =
    [
        new() { Title = "Focus 2026: World Film Market Trends", Year = 2026, Publisher = "European Audiovisual Observatory", DocumentType = "Annual Industry Report", Region = "Europe / Global", Category = "Film & TV", Subtopic = "Global Film Industry", Url = "https://www.obs.coe.int/en/web/observatoire/publications" },
        new() { Title = "Key Trends 2026: Film, Television & Streaming", Year = 2026, Publisher = "European Audiovisual Observatory", DocumentType = "Industry Trends Report", Region = "Europe", Category = "Film & TV", Subtopic = "Film, Television & Streaming", Url = "https://www.obs.coe.int/en/web/observatoire/industry/key-trends" },
        new() { Title = "Focus 2025: World Film Market Trends", Year = 2025, Publisher = "European Audiovisual Observatory", DocumentType = "Annual Industry Report", Region = "Europe / Global", Category = "Film & TV", Subtopic = "Global Film Market", Url = "https://www.obs.coe.int/en/web/observatoire/publications" },
        new() { Title = "The 2025 European Media Industry Outlook", Year = 2025, Publisher = "European Commission", DocumentType = "Industry Outlook Report", Region = "European Union", Category = "Film & TV", Subtopic = "Film, TV, Video Games & News Media", Url = "https://digital-strategy.ec.europa.eu/en/library/2025-european-media-industry-outlook-report" },
        new() { Title = "Green Transition in the Audiovisual Sector", Year = 2025, Publisher = "European Audiovisual Observatory", DocumentType = "Research Report", Region = "Europe", Category = "Film & TV", Subtopic = "Sustainable Film Production", Url = "https://www.obs.coe.int/en/web/observatoire/publications" },
        new() { Title = "Writers and Directors of Film and TV/SVOD Fiction (2015–2024 Figures)", Year = 2026, Publisher = "European Audiovisual Observatory", DocumentType = "Research Report", Region = "Europe", Category = "Film & TV", Subtopic = "Film Production", Url = "https://www.obs.coe.int/en/web/observatoire/publications" },
    ];

    // ── Education ─────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Education =
    [
        new() { Title = "Global Education Monitoring Report 2026: Access and Equity", Year = 2026, Publisher = "UNESCO", DocumentType = "Flagship Global Report", Region = "Global", Category = "Education", Subtopic = "Education Access & Equity", Url = "https://www.unesco.org/gem-report/en/publication/equity-and-access" },
        new() { Title = "Global Education Monitoring Report 2025: Education and Justice", Year = 2025, Publisher = "UNESCO", DocumentType = "Flagship Global Report", Region = "Global", Category = "Education", Subtopic = "Education Policy", Url = "https://www.unesco.org/gem-report/en" },
        new() { Title = "Education Finance Watch 2025", Year = 2025, Publisher = "UNESCO & World Bank", DocumentType = "Joint Research Report", Region = "Global", Category = "Education", Subtopic = "Education Financing", Url = "https://www.unesco.org/gem-report/en/publications" },
        new() { Title = "OECD Education at a Glance 2025", Year = 2025, Publisher = "OECD", DocumentType = "Annual Statistical Report", Region = "Global", Category = "Education", Subtopic = "Education Systems & Indicators", Url = "https://www.oecd.org/education/education-at-a-glance/" },
        new() { Title = "OECD Education Policy Outlook 2025", Year = 2025, Publisher = "OECD", DocumentType = "Policy Report", Region = "Global", Category = "Education", Subtopic = "Education Reform", Url = "https://www.oecd.org/education/policy-outlook/" },
        new() { Title = "UNICEF Annual Report 2025", Year = 2025, Publisher = "UNICEF", DocumentType = "Annual Report", Region = "Global", Category = "Education", Subtopic = "Children & Education", Url = "https://www.unicef.org/reports/annual-report-2025" },
        new() { Title = "World Bank Education Publications 2020–2026", Year = 2025, Publisher = "World Bank", DocumentType = "Research Collection", Region = "Global", Category = "Education", Subtopic = "Education Development", Url = "https://www.worldbank.org/en/topic/education/publications" },
    ];

    // ── Fashion ───────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Fashion =
    [
        new() { Title = "The State of Fashion 2026: When the Rules Change", Year = 2026, Publisher = "BoF & McKinsey", DocumentType = "Annual Industry Report", Region = "Global", Category = "Fashion", Subtopic = "Global Fashion Industry", Url = "https://www.mckinsey.com/industries/retail/our-insights/state-of-fashion-2026" },
        new() { Title = "The State of Fashion 2025: Challenges at Every Turn", Year = 2025, Publisher = "BoF & McKinsey", DocumentType = "Annual Industry Report", Region = "Global", Category = "Fashion", Subtopic = "Fashion Industry Outlook", Url = "https://www.mckinsey.com/industries/retail/our-insights/state-of-fashion-2025" },
        new() { Title = "The State of Fashion: Luxury 2025", Year = 2025, Publisher = "BoF & McKinsey", DocumentType = "Special Industry Report", Region = "Global", Category = "Fashion", Subtopic = "Luxury Fashion", Url = "https://www.businessoffashion.com/reports/luxury/the-state-of-fashion-luxury-bof-mckinsey-report-special-edition-2025/" },
        new() { Title = "The State of Fashion 2024: Riding Out the Storm", Year = 2024, Publisher = "BoF & McKinsey", DocumentType = "Annual Industry Report", Region = "Global", Category = "Fashion", Subtopic = "Fashion Market", Url = "https://www.mckinsey.com/industries/retail/our-insights/state-of-fashion-archive" },
        new() { Title = "Fashion Industry in the Age of Generative AI and Metaverse", Year = 2025, Publisher = "arXiv", DocumentType = "Research Paper", Region = "Global", Category = "Fashion", Subtopic = "AI & Fashion", Url = "https://arxiv.org/abs/2505.17141" },
        new() { Title = "World Intellectual Property Indicators 2025 (Fashion & IP)", Year = 2025, Publisher = "WIPO", DocumentType = "Annual Report", Region = "Global", Category = "Fashion", Subtopic = "Fashion Design & Intellectual Property", Url = "https://www.wipo.int/publications/en/details.jsp?id=4700" },
    ];

    // ── Lifestyle ─────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Lifestyle =
    [
        new() { Title = "World Happiness Report 2025", Year = 2025, Publisher = "Wellbeing Research Centre, Oxford; Gallup; SDSN", DocumentType = "Annual Research Report", Region = "Global", Category = "Lifestyle", Subtopic = "Well-being, Quality of Life, Social Connection", Url = "https://worldhappiness.report/ed/2025/" },
        new() { Title = "World Social Report 2025: A New Policy Consensus to Accelerate Social Progress", Year = 2025, Publisher = "UN DESA", DocumentType = "Flagship Social Development Report", Region = "Global", Category = "Lifestyle", Subtopic = "Social Development, Inequality, Living Conditions", Url = "https://desapublications.un.org/publications/world-social-report-2025-new-policy-consensus-accelerate-social-progress" },
        new() { Title = "Human Development Report 2025: People and Possibilities in the Age of AI", Year = 2025, Publisher = "UNDP", DocumentType = "Flagship Report", Region = "Global", Category = "Lifestyle", Subtopic = "Human Development, Technology, Quality of Life", Url = "https://hdr.undp.org/content/human-development-report-2025" },
        new() { Title = "Global Gender Gap Report 2025", Year = 2025, Publisher = "World Economic Forum", DocumentType = "Annual Research Report", Region = "Global", Category = "Lifestyle", Subtopic = "Gender, Society, Economic Participation", Url = "https://www.weforum.org/publications/global-gender-gap-report-2025/" },
        new() { Title = "OECD Society at a Glance 2024", Year = 2024, Publisher = "OECD", DocumentType = "Social Indicators Report", Region = "Global", Category = "Lifestyle", Subtopic = "Social Conditions, Families, Communities", Url = "https://www.oecd.org/en/publications/society-at-a-glance-2024_918d8d4b-en.html" },
    ];

    // ── Faith / Religion ──────────────────────────────────────────────────────

    private static readonly LibraryDocument[] FaithReligion =
    [
        new() { Title = "Global Religious Landscape: Religious Composition by Country", Year = 2024, Publisher = "Pew Research Center", DocumentType = "Research Report", Region = "Global", Category = "Faith & Religion", Subtopic = "Religious Demographics, Global Faith Trends", Url = "https://www.pewresearch.org/religion/2024/06/12/religious-composition-by-country-2010-2020/" },
        new() { Title = "International Religious Freedom Report 2024", Year = 2025, Publisher = "U.S. Department of State", DocumentType = "Government Research Report", Region = "Global", Category = "Faith & Religion", Subtopic = "Religious Freedom, Faith Communities, Human Rights", Url = "https://www.state.gov/reports/2024-report-on-international-religious-freedom/" },
        new() { Title = "Annual Report of USCIRF 2025", Year = 2025, Publisher = "U.S. Commission on International Religious Freedom", DocumentType = "Policy Report", Region = "Global", Category = "Faith & Religion", Subtopic = "Religious Freedom & Persecution", Url = "https://www.uscirf.gov/reports-briefs/annual-report" },
        new() { Title = "Religion, Peace and Security Research Publications", Year = 2025, Publisher = "United States Institute of Peace", DocumentType = "Research Papers & Policy Reports", Region = "Global", Category = "Faith & Religion", Subtopic = "Religion, Conflict Resolution, Peacebuilding", Url = "https://www.usip.org/publications" },
    ];

    // ── Philosophy ────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Philosophy =
    [
        new() { Title = "Philosophy and Phenomenological Research — Volume 111, Issue 1 (2025)", Year = 2025, Publisher = "Wiley / PPR, Inc.", DocumentType = "Academic Philosophy Journal Issue", Region = "Global", Category = "Philosophy", Subtopic = "Metaphysics, Ethics, Epistemology, Philosophy of Mind", Url = "https://onlinelibrary.wiley.com/toc/19331592/2025/111/1" },
        new() { Title = "Philosophy for and by Everyone: How Doing Philosophy Supports Epistemic Agency", Year = 2025, Publisher = "King's College London / Revue Internationale de Philosophie", DocumentType = "Peer-reviewed Research Article", Region = "Global", Category = "Philosophy", Subtopic = "Philosophy Education, Public Philosophy, Knowledge", Url = "https://kclpure.kcl.ac.uk/portal/en/publications/philosophy-for-and-by-everyone-how-doing-philosophy-supports-epis/" },
        new() { Title = "Stanford Encyclopedia of Philosophy (SEP) — 2025 Updates", Year = 2025, Publisher = "Stanford University", DocumentType = "Research Reference Database", Region = "Global", Category = "Philosophy", Subtopic = "All Philosophy Fields", Url = "https://plato.stanford.edu/" },
        new() { Title = "PhilReport #1 — Philosophy Research Infrastructure", Year = 2025, Publisher = "FID Philosophie / University of Cologne", DocumentType = "Research Community Publication", Region = "Europe", Category = "Philosophy", Subtopic = "Philosophy Research Infrastructure, Open Access", Url = "https://kups.ub.uni-koeln.de/75207/" },
    ];

    // ── Environment ───────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Environment =
    [
        new() { Title = "Global Environment Outlook 7 (GEO-7): A Future We Choose", Year = 2025, Publisher = "UNEP", DocumentType = "Flagship Scientific Assessment Report", Region = "Global", Category = "Environment", Subtopic = "Environmental Change, Sustainability, Pollution, Biodiversity", Url = "https://www.unep.org/resources/global-environment-outlook-7" },
        new() { Title = "Frontiers 2025: The Weight of Time – Facing a New Age of Challenges", Year = 2025, Publisher = "UNEP", DocumentType = "Emerging Environmental Issues Report", Region = "Global", Category = "Environment", Subtopic = "Emerging Environmental Risks, Ecosystems", Url = "https://www.unep.org/resources/frontiers-2025-weight-time" },
        new() { Title = "Global Biodiversity Outlook 5 (GBO-5)", Year = 2020, Publisher = "CBD / UNEP", DocumentType = "Flagship Biodiversity Assessment Report", Region = "Global", Category = "Environment", Subtopic = "Biodiversity Loss, Ecosystem Conservation", Url = "https://www.cbd.int/gbo5" },
        new() { Title = "Global Resources Outlook 2024: Bend the Trend", Year = 2024, Publisher = "IRP / UNEP", DocumentType = "Scientific Assessment Report", Region = "Global", Category = "Environment", Subtopic = "Resource Consumption, Circular Economy", Url = "https://www.resourcepanel.org/reports/global-resources-outlook-2024" },
        new() { Title = "Global Assessment Report on Biodiversity and Ecosystem Services (IPBES)", Year = 2019, Publisher = "IPBES", DocumentType = "Scientific Assessment Report", Region = "Global", Category = "Environment", Subtopic = "Biodiversity, Ecosystem Services, Human Impact", Url = "https://www.ipbes.net/global-assessment" },
    ];

    // ── Travel ────────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Travel =
    [
        new() { Title = "International Tourism Highlights 2025 Edition", Year = 2025, Publisher = "UN Tourism", DocumentType = "Statistical Report", Region = "Global", Category = "Travel", Subtopic = "International Tourism Trends, Arrivals, Destinations", Url = "https://en.unwto-ap.org/news/th2025/" },
        new() { Title = "Travel & Tourism Economic Impact 2025: Global Trends", Year = 2025, Publisher = "WTTC", DocumentType = "Economic Impact Report", Region = "Global", Category = "Travel", Subtopic = "Tourism GDP Contribution, Employment, Investment", Url = "https://wttc.org/research/economic-impact" },
        new() { Title = "World Air Transport Statistics (WATS) 2025", Year = 2025, Publisher = "IATA", DocumentType = "Aviation Statistics Report", Region = "Global", Category = "Travel", Subtopic = "Passenger Demand, Airline Performance, Aviation Economics", Url = "https://www.iata.org/en/services/data/market-data/world-air-transport-statistics/" },
        new() { Title = "OECD Tourism Trends and Policies 2024", Year = 2024, Publisher = "OECD", DocumentType = "Policy Report", Region = "Global", Category = "Travel", Subtopic = "Tourism Policy, Sustainability, Destination Management", Url = "https://www.oecd.org/cfe/tourism/oecd-tourism-trends-and-policies.htm" },
    ];

    // ── Medicine ──────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Medicine =
    [
        new() { Title = "World Health Statistics 2025", Year = 2025, Publisher = "WHO", DocumentType = "Annual Health Statistics Report", Region = "Global", Category = "Medicine", Subtopic = "Global Health Indicators, Disease Burden, Health Systems", Url = "https://www.who.int/data/gho/publications/world-health-statistics" },
        new() { Title = "WHO Results Report 2025", Year = 2026, Publisher = "WHO", DocumentType = "Institutional Health Impact Report", Region = "Global", Category = "Medicine", Subtopic = "Universal Health Coverage, Health Emergencies", Url = "https://www.who.int/publications/i/item/9789240115538" },
        new() { Title = "The Selection and Use of Essential Medicines 2025", Year = 2025, Publisher = "WHO", DocumentType = "Technical Medical Report", Region = "Global", Category = "Medicine", Subtopic = "Essential Medicines, Pharmacology, Public Health", Url = "https://iris.who.int/" },
        new() { Title = "Global Report on Hypertension: The Race Against a Silent Killer", Year = 2023, Publisher = "WHO", DocumentType = "Medical Disease Report", Region = "Global", Category = "Medicine", Subtopic = "Cardiovascular Medicine, Chronic Disease", Url = "https://www.who.int/publications/i/item/9789240081062" },
        new() { Title = "Global Status Report on Preventing Violence Against Children 2024", Year = 2024, Publisher = "WHO, UNICEF, UNESCO, UNODC", DocumentType = "Global Health Report", Region = "Global", Category = "Medicine", Subtopic = "Child Health, Violence Prevention, Public Health", Url = "https://www.who.int/publications/i/item/9789240091319" },
    ];

    // ── Real Estate ───────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] RealEstate =
    [
        new() { Title = "Emerging Trends in Real Estate® Global Outlook 2025", Year = 2025, Publisher = "PwC & Urban Land Institute", DocumentType = "Industry Research Report", Region = "Global", Category = "Real Estate", Subtopic = "Real Estate Investment, Property Markets, Future Trends", Url = "https://www.pwc.com/gx/en/industries/financial-services/real-estate/emerging-trends-real-estate/etre-global-outlook.html" },
        new() { Title = "Emerging Trends in Real Estate® 2025 (United States & Canada)", Year = 2025, Publisher = "PwC & Urban Land Institute", DocumentType = "Market Outlook Report", Region = "North America", Category = "Real Estate", Subtopic = "Commercial Real Estate, Investment, Urban Development", Url = "https://www.pwc.com/us/en/library/emerging-trends-real-estate.html" },
        new() { Title = "Emerging Trends in Real Estate® Europe 2025", Year = 2025, Publisher = "PwC & Urban Land Institute", DocumentType = "Regional Real Estate Report", Region = "Europe", Category = "Real Estate", Subtopic = "Property Investment, Cities, Development Trends", Url = "https://www.pwc.com/gx/en/industries/financial-services/real-estate/emerging-trends-real-estate/europe-2025.html" },
        new() { Title = "Global Real Estate Transparency Index 2024", Year = 2024, Publisher = "JLL", DocumentType = "Research Report", Region = "Global", Category = "Real Estate", Subtopic = "Real Estate Markets, Investment Transparency, Governance", Url = "https://www.jll.com/en/trends-and-insights/research/global-real-estate-transparency-index" },
        new() { Title = "Global Real Estate Market Outlook 2025", Year = 2025, Publisher = "CBRE", DocumentType = "Market Outlook Report", Region = "Global", Category = "Real Estate", Subtopic = "Commercial Real Estate, Capital Markets, Property Investment", Url = "https://www.cbre.com/insights/books/global-real-estate-market-outlook-2025" },
        new() { Title = "World Cities Report 2024: Cities and Climate Action", Year = 2024, Publisher = "UN-Habitat", DocumentType = "Flagship Urban Development Report", Region = "Global", Category = "Real Estate", Subtopic = "Urban Housing, Sustainable Cities, Real Estate Development", Url = "https://unhabitat.org/wcr/" },
        new() { Title = "Global Housing Watch (IMF)", Year = 2025, Publisher = "IMF", DocumentType = "Housing Market Research Database", Region = "Global", Category = "Real Estate", Subtopic = "Housing Prices, Mortgage Markets, Financial Stability", Url = "https://www.imf.org/en/Research/IMF-Notes/Issues/2024/02/29/Global-Housing-Watch" },
        new() { Title = "On the Performance of LLMs for Real Estate Appraisal", Year = 2025, Publisher = "arXiv", DocumentType = "Research Paper", Region = "Global", Category = "Real Estate", Subtopic = "Artificial Intelligence in Real Estate, Property Valuation", Url = "https://arxiv.org/abs/2506.11812" },
    ];

    // ── Law ───────────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Law =
    [
        new() { Title = "Rule of Law Index 2025", Year = 2025, Publisher = "World Justice Project", DocumentType = "Global Legal Governance Report", Region = "Global", Category = "Law", Subtopic = "Rule of Law, Justice Systems, Governance, Corruption", Url = "https://worldjusticeproject.org/rule-of-law-index" },
        new() { Title = "World Justice Project Rule of Law Index 2024", Year = 2024, Publisher = "World Justice Project", DocumentType = "Annual Research Report", Region = "Global", Category = "Law", Subtopic = "Legal Systems, Civil Justice, Criminal Justice", Url = "https://worldjusticeproject.org/rule-of-law-index/downloads/WJPIndex2024.pdf" },
        new() { Title = "UNODC Global Study on Homicide 2023", Year = 2023, Publisher = "UNODC", DocumentType = "Legal / Criminal Justice Research Report", Region = "Global", Category = "Law", Subtopic = "Criminal Law, Crime Prevention, Justice Systems", Url = "https://www.unodc.org/unodc/en/data-and-analysis/global-study-on-homicide.html" },
        new() { Title = "Global Report on Trafficking in Persons 2024", Year = 2024, Publisher = "UNODC", DocumentType = "Criminal Justice Report", Region = "Global", Category = "Law", Subtopic = "Human Trafficking Law, Criminal Networks, Victim Protection", Url = "https://www.unodc.org/unodc/en/data-and-analysis/glotip.html" },
        new() { Title = "AI and the Rule of Law: Emerging Legal Challenges", Year = 2024, Publisher = "Council of Europe", DocumentType = "Legal Policy Research", Region = "Europe / Global", Category = "Law", Subtopic = "AI Law, Human Rights, Technology Regulation", Url = "https://www.coe.int/en/web/artificial-intelligence" },
        new() { Title = "Justice for All Report: Final Report of the Task Force on Justice", Year = 2021, Publisher = "Pathfinders for Peaceful, Just and Inclusive Societies", DocumentType = "Justice Research Report", Region = "Global", Category = "Law", Subtopic = "Access to Justice, Legal Empowerment", Url = "https://www.justice.sdg16.plus/" },
        new() { Title = "World Intellectual Property Indicators 2025 (IP Law)", Year = 2025, Publisher = "WIPO", DocumentType = "IP Law Statistics Report", Region = "Global", Category = "Law", Subtopic = "Patents, Copyright, Trademarks, Innovation Law", Url = "https://www.wipo.int/publications/en/details.jsp?id=4721" },
    ];

    // ── Books & Literature ────────────────────────────────────────────────────

    private static readonly LibraryDocument[] Literature =
    [
        new() { Title = "UNESCO World Book Capital Annual Reports and Publications", Year = 2025, Publisher = "UNESCO", DocumentType = "Cultural Research Reports", Region = "Global", Category = "Books & Literature", Subtopic = "Reading Culture, Publishing, Literacy, Literary Development", Url = "https://www.unesco.org/en/world-book-capital" },
        new() { Title = "IPA Global Publishing Statistics Report 2024", Year = 2024, Publisher = "International Publishers Association", DocumentType = "Industry Statistics Report", Region = "Global", Category = "Books & Literature", Subtopic = "Publishing Revenue, Book Markets, Industry Performance", Url = "https://www.internationalpublishers.org/resources/global-publishing-statistics/" },
        new() { Title = "Re|Shaping Policies for Creativity: Culture as a Global Public Good", Year = 2022, Publisher = "UNESCO", DocumentType = "Global Cultural Report", Region = "Global", Category = "Books & Literature", Subtopic = "Creative Industries, Publishing, Cultural Expression", Url = "https://unesdoc.unesco.org/ark:/48223/pf0000380474" },
        new() { Title = "PEN America Literary Reports", Year = 2025, Publisher = "PEN America", DocumentType = "Literary Research Reports", Region = "Global", Category = "Books & Literature", Subtopic = "Censorship, Freedom to Read, Writers' Rights", Url = "https://pen.org/reports/" },
    ];

    // ── History ───────────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] History =
    [
        new() { Title = "UNESCO General History of Africa (Digital Publications & Research Updates)", Year = 2025, Publisher = "UNESCO", DocumentType = "Historical Research Publication Series", Region = "Africa / Global", Category = "History", Subtopic = "African History, Heritage, Civilisation", Url = "https://www.unesco.org/en/general-history-africa", IsAfrica = true },
        new() { Title = "UNESCO World Heritage Reports and Research Publications", Year = 2025, Publisher = "UNESCO World Heritage Centre", DocumentType = "Heritage Research Reports", Region = "Global", Category = "History", Subtopic = "Cultural Heritage, Historical Sites, Archaeology, Conservation History", Url = "https://whc.unesco.org/en/publications/" },
        new() { Title = "The State of the World's Indigenous Peoples: Indigenous Peoples and Historical Justice", Year = 2021, Publisher = "UN DESA", DocumentType = "Research Report", Region = "Global", Category = "History", Subtopic = "Indigenous History, Colonial Legacy, Cultural Rights", Url = "https://social.desa.un.org/publications/state-of-the-worlds-indigenous-peoples" },
        new() { Title = "International Review of Social History (Recent Issues)", Year = 2025, Publisher = "Cambridge University Press / IISH", DocumentType = "Historical Research Journal", Region = "Global", Category = "History", Subtopic = "Social History, Labour History, Political History", Url = "https://www.cambridge.org/core/journals/international-review-of-social-history" },
    ];

    // ── Gaming & Esports ──────────────────────────────────────────────────────

    private static readonly LibraryDocument[] GamingEsports =
    [
        new() { Title = "Global Games Market Report 2025", Year = 2025, Publisher = "Newzoo", DocumentType = "Gaming Industry Research Report", Region = "Global", Category = "Gaming & Esports", Subtopic = "Game Revenue, Players, Platforms, Market Forecasts", Url = "https://newzoo.com/reports/global-games-market-report" },
        new() { Title = "Global Games Market Report 2025 – Free Edition", Year = 2025, Publisher = "Newzoo", DocumentType = "Market Intelligence Report", Region = "Global", Category = "Gaming & Esports", Subtopic = "PC Gaming, Console Gaming, Mobile Gaming, Regional Markets", Url = "https://newzoo.com/articles/global-games-market-189-billion-2025" },
        new() { Title = "Mapping IP Fandom with the Global Gamer Study 2025", Year = 2025, Publisher = "Newzoo", DocumentType = "Gamer Behaviour Research Report", Region = "Global", Category = "Gaming & Esports", Subtopic = "Gaming Communities, Entertainment Franchises, Player Engagement", Url = "https://newzoo.com/resources/trend-reports/mapping-ip-fandom-with-the-global-gamer-study-2025-free-report" },
        new() { Title = "Esports and Expertise: What Competitive Gaming Can Teach Us About Mastery", Year = 2025, Publisher = "arXiv", DocumentType = "Research Paper", Region = "Global", Category = "Gaming & Esports", Subtopic = "Esports Performance, Competitive Gaming, Learning", Url = "https://arxiv.org/abs/2507.05446" },
        new() { Title = "PandaSkill: Player Performance and Skill Rating in Esports — League of Legends", Year = 2025, Publisher = "arXiv", DocumentType = "Research Paper", Region = "Global", Category = "Gaming & Esports", Subtopic = "Esports Analytics, Machine Learning, Player Ranking Systems", Url = "https://arxiv.org/abs/2501.10049" },
        new() { Title = "Essential Facts About the Video Game Industry 2025", Year = 2025, Publisher = "Entertainment Software Association (ESA)", DocumentType = "Industry Research Report", Region = "United States", Category = "Gaming & Esports", Subtopic = "Gamer Demographics, Consumer Behaviour, Game Industry Trends", Url = "https://www.theesa.com/resources/" },
    ];

    // ── Art & Craft ───────────────────────────────────────────────────────────

    private static readonly LibraryDocument[] ArtCraft =
    [
        new() { Title = "Re|Shaping Policies for Creativity: Addressing Culture as a Global Public Good", Year = 2022, Publisher = "UNESCO", DocumentType = "Global Cultural Policy Report", Region = "Global", Category = "Art & Craft", Subtopic = "Creative Industries, Arts Ecosystems, Cultural Production", Url = "https://unesdoc.unesco.org/ark:/48223/pf0000380474" },
        new() { Title = "Creative Economy Outlook 2024", Year = 2024, Publisher = "UNCTAD", DocumentType = "Global Creative Economy Report", Region = "Global", Category = "Art & Craft", Subtopic = "Arts Markets, Crafts, Cultural Goods, Creative Entrepreneurship", Url = "https://unctad.org/publication/creative-economy-outlook-2024" },
        new() { Title = "World Intellectual Property Report 2024: Making Creativity Count", Year = 2024, Publisher = "WIPO", DocumentType = "IP & Creative Economy Research Report", Region = "Global", Category = "Art & Craft", Subtopic = "Creativity, Copyright, Design, Innovation", Url = "https://www.wipo.int/publications/en/details.jsp?id=4781" },
        new() { Title = "Cultural and Creative Industries in the Face of COVID-19: An Economic Impact Outlook", Year = 2021, Publisher = "UNESCO", DocumentType = "Cultural Sector Research Report", Region = "Global", Category = "Art & Craft", Subtopic = "Artists, Creative Businesses, Cultural Production", Url = "https://unesdoc.unesco.org/ark:/48223/pf0000377863" },
    ];
}
