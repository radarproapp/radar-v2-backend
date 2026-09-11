using RadarV2.Models;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// Seed data for sources and topics. Populated on first access when the
/// collections are empty, similar to LibraryRegistry.
/// </summary>
public static class SourceSeedData
{
    public static readonly List<SourceProfile> All =
    [
        // ── AI & Technology ──────────────────────────────────────────────
        new SourceProfile
        {
            Id = "src-openai",
            Name = "OpenAI",
            Abbreviation = "OAI",
            Type = "Primary research publisher",
            Domain = "openai.com",
            TrustNote = "A primary source publishing about its own research and products. Radar treats OpenAI as authoritative on its own systems, and corroborates broader industry claims against independent sources before publishing them.",
            AiEdge = "This source drives most of what changes in AI Fundamentals — the module on your roadmap. Their announcements tend to lead hiring requirements by roughly a year.",
            ItemsInRadar = 18,
            ItemsRead = 5,
            PublishFrequency = "2/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = true,
        },
        new SourceProfile
        {
            Id = "src-anthropic",
            Name = "Anthropic",
            Abbreviation = "ANT",
            Type = "Primary research publisher",
            Domain = "anthropic.com",
            TrustNote = "Publishes original AI safety research and model cards. Treated as authoritative on its own systems; broader claims corroborated independently.",
            AiEdge = "Their safety-focused approach shapes how responsible AI is discussed in interviews and policy circles.",
            ItemsInRadar = 12,
            ItemsRead = 3,
            PublishFrequency = "1/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = true,
        },
        new SourceProfile
        {
            Id = "src-mit-tech-review",
            Name = "MIT Technology Review",
            Abbreviation = "MIT",
            Type = "Technology journalism",
            Domain = "technologyreview.com",
            TrustNote = "High editorial standards. Independent reporting on technology with academic rigour.",
            AiEdge = "Covers AI, climate tech, and biotech at a level that bridges research and policy — useful for understanding how technology trends translate to career opportunities.",
            ItemsInRadar = 24,
            ItemsRead = 8,
            PublishFrequency = "3/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = true,
        },
        new SourceProfile
        {
            Id = "src-the-verge",
            Name = "The Verge",
            Abbreviation = "Verge",
            Type = "Technology journalism",
            Domain = "theverge.com",
            TrustNote = "Major technology publication with strong product and consumer tech coverage.",
            AiEdge = "Good for staying current on product launches and consumer tech trends that affect product management roles.",
            ItemsInRadar = 15,
            ItemsRead = 4,
            PublishFrequency = "daily",
            IsFollowing = false,
            IncludeInWeeklyBrief = false,
        },
        new SourceProfile
        {
            Id = "src-wired",
            Name = "WIRED",
            Abbreviation = "Wired",
            Type = "Technology journalism",
            Domain = "wired.com",
            TrustNote = "Long-form technology and culture journalism. Strong editorial process.",
            AiEdge = "Deep dives into how technology intersects with society — useful for building strategic thinking.",
            ItemsInRadar = 20,
            ItemsRead = 6,
            PublishFrequency = "3/wk",
            IsFollowing = false,
            IncludeInWeeklyBrief = false,
        },

        // ── Policy & Global Affairs ──────────────────────────────────────
        new SourceProfile
        {
            Id = "src-worldbank",
            Name = "World Bank",
            Abbreviation = "WB",
            Type = "Global institution",
            Domain = "worldbank.org",
            TrustNote = "Primary data and research on development economics. Authoritative on global development statistics.",
            AiEdge = "Their digital infrastructure research directly informs the policy module on your roadmap.",
            ItemsInRadar = 30,
            ItemsRead = 10,
            PublishFrequency = "2/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = true,
        },
        new SourceProfile
        {
            Id = "src-chatham-house",
            Name = "Chatham House",
            Abbreviation = "CH",
            Type = "Think tank",
            Domain = "chathamhouse.org",
            TrustNote = "Leading international affairs think tank. Research is peer-reviewed and well-sourced.",
            AiEdge = "Their Africa-focused policy papers are directly relevant to understanding governance and security trends.",
            ItemsInRadar = 16,
            ItemsRead = 5,
            PublishFrequency = "1/wk",
            IsFollowing = false,
            IncludeInWeeklyBrief = false,
        },
        new SourceProfile
        {
            Id = "src-aeon",
            Name = "Aeon",
            Abbreviation = "Aeon",
            Type = "Ideas publication",
            Domain = "aeon.co",
            TrustNote = "High-quality essays on philosophy, science, and society. Rigorous editorial process.",
            AiEdge = "Builds the kind of long-form thinking that distinguishes strong product strategists.",
            ItemsInRadar = 14,
            ItemsRead = 7,
            PublishFrequency = "3/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = false,
        },

        // ── Finance & Business ───────────────────────────────────────────
        new SourceProfile
        {
            Id = "src-stripe",
            Name = "Stripe",
            Abbreviation = "Stripe",
            Type = "Company engineering blog",
            Domain = "stripe.com/blog",
            TrustNote = "First-party engineering insights from a major payments infrastructure company.",
            AiEdge = "Payments infrastructure knowledge is directly relevant to fintech product roles.",
            ItemsInRadar = 10,
            ItemsRead = 4,
            PublishFrequency = "1/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = true,
        },
        new SourceProfile
        {
            Id = "src-the-economist",
            Name = "The Economist",
            Abbreviation = "Econ",
            Type = "Business journalism",
            Domain = "economist.com",
            TrustNote = "Global business and economics coverage with high editorial standards.",
            AiEdge = "Macroeconomic context that helps you understand market conditions for product decisions.",
            ItemsInRadar = 28,
            ItemsRead = 12,
            PublishFrequency = "daily",
            IsFollowing = false,
            IncludeInWeeklyBrief = false,
        },

        // ── Africa-Focused ───────────────────────────────────────────────
        new SourceProfile
        {
            Id = "src-disrupt-africa",
            Name = "Disrupt Africa",
            Abbreviation = "DA",
            Type = "Startup journalism",
            Domain = "disruptafrica.com",
            TrustNote = "Leading African startup ecosystem coverage. Well-connected to the ecosystem.",
            AiEdge = "Tracks which African startups are raising and hiring — direct signal for career opportunities.",
            ItemsInRadar = 22,
            ItemsRead = 9,
            PublishFrequency = "3/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = true,
        },
        new SourceProfile
        {
            Id = "src-technext",
            Name = "Technext",
            Abbreviation = "TN",
            Type = "Nigerian tech journalism",
            Domain = "technext24.com",
            TrustNote = "Nigeria-focused technology and startup news. Strong local network.",
            AiEdge = "Covers the Nigerian tech ecosystem at a granular level — who is hiring, what is launching.",
            ItemsInRadar = 18,
            ItemsRead = 6,
            PublishFrequency = "3/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = false,
        },

        // ── Research & Academic ──────────────────────────────────────────
        new SourceProfile
        {
            Id = "src-nature",
            Name = "Nature",
            Abbreviation = "Nature",
            Type = "Scientific journal",
            Domain = "nature.com",
            TrustNote = "World's leading multidisciplinary science journal. Peer-reviewed.",
            AiEdge = "Breakthrough research that often precedes industry trends by 1-2 years.",
            ItemsInRadar = 40,
            ItemsRead = 8,
            PublishFrequency = "daily",
            IsFollowing = false,
            IncludeInWeeklyBrief = false,
        },
        new SourceProfile
        {
            Id = "src-lenny-podcast",
            Name = "Lenny's Podcast",
            Abbreviation = "Lenny",
            Type = "Product management podcast",
            Domain = "lennyspodcast.com",
            TrustNote = "Top product management podcast. Guests are senior practitioners at leading companies.",
            AiEdge = "Directly relevant to product management career prep — interview insights and frameworks.",
            ItemsInRadar = 8,
            ItemsRead = 3,
            PublishFrequency = "1/wk",
            IsFollowing = true,
            IncludeInWeeklyBrief = true,
        },
        new SourceProfile
        {
            Id = "src-mit-ocw",
            Name = "MIT OpenCourseWare",
            Abbreviation = "MIT OCW",
            Type = "Educational content",
            Domain = "ocw.mit.edu",
            TrustNote = "Free course materials from MIT. Authoritative educational content.",
            AiEdge = "Structured learning paths that map directly to your roadmap modules.",
            ItemsInRadar = 6,
            ItemsRead = 2,
            PublishFrequency = "monthly",
            IsFollowing = true,
            IncludeInWeeklyBrief = false,
        },
    ];

    public static readonly List<TopicProfile> Topics =
    [
        new TopicProfile
        {
            Id = "topic-ai",
            Name = "AI Fundamentals",
            Summary = "The bottleneck has moved from model capability to deployment reliability.",
            SummarySource = "Synthesised from 7 sources · updated 3h ago",
            SummaryUpdatedAt = DateTime.UtcNow.AddHours(-3),
            AiEdge = "This is your top interest and it is already on your roadmap. Understanding deployment reliability — rather than model theory — is what separates candidates in AI-adjacent hiring right now.",
            ItemsInRadar = 31,
            ItemsThisWeek = 4,
            SourceCount = 7,
            IsFollowing = true,
            OnRoadmap = true,
            RoadmapModuleName = "AI Fundamentals · module 2 of 6",
            RoadmapContents = ["6 articles", "3 podcasts", "5 videos", "8 papers", "1 project"],
            Alerts =
            [
                new TopicAlert { Label = "A primary source publishes", Note = "About 3 a week", IsEnabled = true },
                new TopicAlert { Label = "A peer-reviewed paper appears", Note = "Roughly weekly", IsEnabled = true },
                new TopicAlert { Label = "Any mention, anywhere", Note = "High volume — 20+ a week", IsEnabled = false },
            ],
        },
        new TopicProfile
        {
            Id = "topic-product",
            Name = "Product Management",
            Summary = "The role is shifting from requirements writer to evidence-based decision maker.",
            SummarySource = "Synthesised from 5 sources · updated 1d ago",
            SummaryUpdatedAt = DateTime.UtcNow.AddDays(-1),
            AiEdge = "Your primary career target. Understanding how the role is evolving gives you an edge in interviews.",
            ItemsInRadar = 22,
            ItemsThisWeek = 3,
            SourceCount = 5,
            IsFollowing = true,
            OnRoadmap = true,
            RoadmapModuleName = "PM Foundations · module 1 of 7",
            RoadmapContents = ["4 articles", "2 podcasts", "3 videos", "1 project"],
            Alerts =
            [
                new TopicAlert { Label = "A primary source publishes", Note = "About 2 a week", IsEnabled = true },
                new TopicAlert { Label = "A peer-reviewed paper appears", Note = "Monthly", IsEnabled = false },
            ],
        },
        new TopicProfile
        {
            Id = "topic-payments",
            Name = "Payments Infrastructure",
            Summary = "Cross-border interoperability is the next frontier after domestic mobile money.",
            SummarySource = "Synthesised from 4 sources · updated 2d ago",
            SummaryUpdatedAt = DateTime.UtcNow.AddDays(-2),
            AiEdge = "Payments infrastructure knowledge is a differentiator for fintech product roles in Africa.",
            ItemsInRadar = 15,
            ItemsThisWeek = 2,
            SourceCount = 4,
            IsFollowing = true,
            OnRoadmap = false,
            Alerts =
            [
                new TopicAlert { Label = "A primary source publishes", Note = "About 1 a week", IsEnabled = true },
                new TopicAlert { Label = "Any mention, anywhere", Note = "Moderate — 5+ a week", IsEnabled = false },
            ],
        },
        new TopicProfile
        {
            Id = "topic-digital-infra",
            Name = "Digital Public Infrastructure",
            Summary = "Identity-first, payments-second, data-sharing-third is the sequencing that works.",
            SummarySource = "Synthesised from 3 sources · updated 5d ago",
            SummaryUpdatedAt = DateTime.UtcNow.AddDays(-5),
            AiEdge = "Understanding DPI sequencing helps you predict which rails will exist in three years.",
            ItemsInRadar = 12,
            ItemsThisWeek = 1,
            SourceCount = 3,
            IsFollowing = false,
            OnRoadmap = false,
            Alerts =
            [
                new TopicAlert { Label = "A primary source publishes", Note = "About 1 a week", IsEnabled = false },
            ],
        },
        new TopicProfile
        {
            Id = "topic-climate",
            Name = "Climate & Energy Transition",
            Summary = "Africa's energy transition is skipping the fossil fuel step entirely in many markets.",
            SummarySource = "Synthesised from 4 sources · updated 1w ago",
            SummaryUpdatedAt = DateTime.UtcNow.AddDays(-7),
            AiEdge = "Climate tech is a growing sector for product roles. Understanding the African angle is a differentiator.",
            ItemsInRadar = 18,
            ItemsThisWeek = 2,
            SourceCount = 4,
            IsFollowing = false,
            OnRoadmap = false,
            Alerts =
            [
                new TopicAlert { Label = "A primary source publishes", Note = "About 2 a week", IsEnabled = false },
            ],
        },
        new TopicProfile
        {
            Id = "topic-health",
            Name = "Digital Health",
            Summary = "Telemedicine adoption surged during COVID and has not retreated — but regulation lags.",
            SummarySource = "Synthesised from 3 sources · updated 3d ago",
            SummaryUpdatedAt = DateTime.UtcNow.AddDays(-3),
            AiEdge = "Health tech is an emerging product management field in Africa with strong funding tailwinds.",
            ItemsInRadar = 14,
            ItemsThisWeek = 1,
            SourceCount = 3,
            IsFollowing = false,
            OnRoadmap = false,
            Alerts =
            [
                new TopicAlert { Label = "A primary source publishes", Note = "About 1 a week", IsEnabled = false },
            ],
        },
    ];
}
