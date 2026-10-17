using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Generates and manages daily signal clips with real playable video and media streams.
/// Clips are bite-sized, playable highlights tailored to the user's primary domain and learning path.
/// </summary>
public class MongoClipsService : IClipsService
{
    private readonly RadarDatabase _db;
    private readonly IAiEngine _ai;
    private readonly ILogger<MongoClipsService> _log;

    public MongoClipsService(RadarDatabase db, IAiEngine ai, ILogger<MongoClipsService> log)
    {
        _db = db;
        _ai = ai;
        _log = log;
    }

    public async Task<List<Clip>> GetDailyClipsAsync(UserProfile profile)
    {
        var today = DateTime.UtcNow.Date;
        var existingClips = await _db.Clips
            .Find(c => c.UserId == profile.Id && c.PublishedAt >= today)
            .ToListAsync();

        // If existing clips have playable media, return them
        if (existingClips.Count > 0 && existingClips.All(c => !string.IsNullOrEmpty(c.MediaUrl)))
            return existingClips;

        // Clean out any legacy unplayable clips for today
        if (existingClips.Count > 0)
        {
            await _db.Clips.DeleteManyAsync(c => c.UserId == profile.Id && c.PublishedAt >= today);
        }

        // Generate rich, playable clips from the user's domain and feed
        var clips = await GenerateClipsFromFeedAsync(profile);

        if (clips.Count > 0)
        {
            await _db.Clips.InsertManyAsync(clips);
        }

        return clips;
    }

    public async Task SaveClipAsync(string userId, string clipId)
    {
        var clip = await _db.Clips.Find(c => c.Id == clipId).FirstOrDefaultAsync();
        if (clip is null) return;

        clip.IsSaved = !clip.IsSaved;
        await _db.Clips.ReplaceOneAsync(c => c.Id == clipId, clip);
    }

    private async Task<List<Clip>> GenerateClipsFromFeedAsync(UserProfile profile)
    {
        var activeRoadmap = await _db.Roadmaps
            .Find(r => r.UserId == profile.Id && r.IsActive)
            .FirstOrDefaultAsync();

        var interestText = $"{profile.PrimaryGoal} {profile.TargetRole} {string.Join(" ", profile.Interests)} {string.Join(" ", profile.DominantInterests)} {activeRoadmap?.Interest} {activeRoadmap?.Title}".ToLowerInvariant();

        bool isData = interestText.Contains("data") || interestText.Contains("analytics") || interestText.Contains("bi") || interestText.Contains("sql");
        bool isAI = interestText.Contains("ai") || interestText.Contains("machine learning") || interestText.Contains("deep learning") || interestText.Contains("llm");
        // Default to Cybersecurity if detected or as primary default
        bool isCyber = interestText.Contains("cyber") || interestText.Contains("security") || interestText.Contains("hack") || interestText.Contains("pen") || (!isData && !isAI);

        var clips = new List<Clip>();

        if (isCyber)
        {
            clips.AddRange(GetCybersecurityClips(profile.Id));
        }
        else if (isData)
        {
            clips.AddRange(GetDataAnalyticsClips(profile.Id));
        }
        else
        {
            clips.AddRange(GetAiAndTechClips(profile.Id));
        }

        // Check if there are video content items in the database to also mix in
        var videoItems = await _db.ContentItems
            .Find(i => i.Type == ContentType.Video && !string.IsNullOrEmpty(i.Url))
            .SortByDescending(i => i.PublishedAt)
            .Limit(2)
            .ToListAsync();

        foreach (var item in videoItems)
        {
            if (clips.Any(c => c.ContentItemId == item.Id || c.MediaUrl == item.Url)) continue;

            clips.Add(new Clip
            {
                UserId = profile.Id,
                Tag = item.Topic ?? "Technology",
                Signal = item.Signal ?? item.Title,
                WhyItMatters = item.WhyItMatters ?? item.AiSummary ?? "Essential concept for modern practitioners.",
                YourEdge = item.PersonalizedWhy ?? "Applying this gives you an immediate technical edge over theoretical understanding.",
                NextMove = "Review the breakdown and test this in your local environment today.",
                Source = $"{item.Source} · Feed Signal",
                ContentItemId = item.Id,
                MediaUrl = item.Url,
                MediaType = "video",
                ThumbnailUrl = item.ThumbnailUrl ?? "https://images.unsplash.com/photo-1550751827-4bd374c3f58b?w=800&auto=format&fit=crop&q=80",
                DurationSeconds = 60,
                PublishedAt = DateTime.UtcNow,
            });
        }

        await PersonalizeClipsWithAiAsync(clips, profile, activeRoadmap);
        return clips;
    }

    private static List<Clip> GetCybersecurityClips(string userId) =>
    [
        new Clip
        {
            UserId = userId,
            Tag = "Network Security",
            Signal = "How packet sniffing in Wireshark exposes unencrypted credentials & malware C2 in 60 seconds",
            WhyItMatters = "Packet analysis is the ground truth of security. Attackers often masquerade malicious C2 beacons as regular HTTPS/DNS traffic; packet inspection lets you see handshake anomalies and DNS query volumes.",
            YourEdge = "You can quickly spot unencrypted tokens, ARP poisoning attempts, and suspicious outbound beaconing without needing a multimillion-dollar commercial appliance.",
            NextMove = "Open Wireshark on your local interface, filter by 'dns || http', and observe your browser's unencrypted handshake metadata.",
            Source = "NetworkChuck · YouTube",
            MediaUrl = "https://www.youtube.com/watch?v=2T_OtuD4tI8",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/2T_OtuD4tI8/hqdefault.jpg",
            DurationSeconds = 60,
            PublishedAt = DateTime.UtcNow,
        },
        new Clip
        {
            UserId = userId,
            Tag = "Ethical Hacking",
            Signal = "Bypassing Web Application Firewalls (WAF) using Double URL Encoding & Comment Fragmentation",
            WhyItMatters = "Legacy WAFs inspect single-decoded input strings. If the backend web server applies a second decoding pass after the WAF check, payload characters bypass filtering and execute directly in the database.",
            YourEdge = "You can write resilient web API endpoints that use strict parameter binding and schema validation rather than placing blind trust in boundary WAFs.",
            NextMove = "Test your test lab endpoints with '%2527' (double-encoded single quote) to observe how your application handles double decoding.",
            Source = "LiveOverflow · Web Security",
            MediaUrl = "https://www.youtube.com/watch?v=iyAyN3GFMqc",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/iyAyN3GFMqc/hqdefault.jpg",
            DurationSeconds = 50,
            PublishedAt = DateTime.UtcNow,
        },
        new Clip
        {
            UserId = userId,
            Tag = "Threat Detection",
            Signal = "Active Directory Lateral Movement: Pass-the-Hash without cracking passwords",
            WhyItMatters = "In over 80% of enterprise ransomware attacks, threat actors dump NTLM hashes directly from LSASS memory and authenticate across domain controllers without ever decrypting plaintext passwords.",
            YourEdge = "You can identify anomalous Kerberos service ticket requests (TGS) and deploy Microsoft LAPS to randomize local administrator credentials across all machines.",
            NextMove = "Check whether Credential Guard is enabled in your domain controller baseline policies.",
            Source = "John Hammond · Security Research",
            MediaUrl = "https://www.youtube.com/watch?v=F07_rV5b9tE",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/F07_rV5b9tE/hqdefault.jpg",
            DurationSeconds = 55,
            PublishedAt = DateTime.UtcNow,
        },
        new Clip
        {
            UserId = userId,
            Tag = "Systems Security",
            Signal = "Why Memory Corruption triggers 70% of high-severity CVEs, and how Rust prevents them",
            WhyItMatters = "Buffer overflows and use-after-free flaws allow attackers to overwrite the instruction pointer (EIP/RIP) and execute shellcode. Modern regulators (CISA, White House) now actively mandate memory safety.",
            YourEdge = "Understanding low-level memory layout (stack, heap, registers) enables you to write exploit-resistant system code and conduct deep binary vulnerability triage.",
            NextMove = "Review CISA's official Software Identification guidance for memory-safe roadmap requirements.",
            Source = "Computerphile · Systems Security",
            MediaUrl = "https://www.youtube.com/watch?v=1S0aBV-Waeo",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/1S0aBV-Waeo/hqdefault.jpg",
            DurationSeconds = 60,
            PublishedAt = DateTime.UtcNow,
        },
        new Clip
        {
            UserId = userId,
            Tag = "Cloud Security",
            Signal = "AWS IAM Privilege Escalation: 21 Dangerous Actions that elevate a guest to Administrator",
            WhyItMatters = "Cloud breaches rarely require zero-days; they exploit overly permissive IAM policies like 'iam:CreateAccessKey' or 'iam:PassRole' that allow a compromised contractor key to assume full root privileges.",
            YourEdge = "You can configure automated IAM policy linters (like PMapper and AWS IAM Access Analyzer) to detect hidden privilege escalation routes before code is committed to production.",
            NextMove = "Audit your AWS cloud accounts for any IAM role pairing 'iam:PassRole' with 'ec2:RunInstances'.",
            Source = "David Bombal · Cloud Security",
            MediaUrl = "https://www.youtube.com/watch?v=3Kq1MIfTWCE",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/3Kq1MIfTWCE/hqdefault.jpg",
            DurationSeconds = 45,
            PublishedAt = DateTime.UtcNow,
        },
        new Clip
        {
            UserId = userId,
            Tag = "AI Security",
            Signal = "Indirect Prompt Injections: Hijacking autonomous agents through untrusted web content",
            WhyItMatters = "When an autonomous AI agent reads emails or scans web pages to summarize information, hidden zero-width text or HTML instructions can reprogram the agent to exfiltrate session keys or send unauthorized emails.",
            YourEdge = "You understand how to build dual-boundary AI architectures where untrusted input is processed in a sandboxed, low-privilege reader agent before actions are executed.",
            NextMove = "Review your application prompts to ensure raw external API inputs are isolated from system instruction blocks.",
            Source = "Andrej Karpathy · Deep Learning",
            MediaUrl = "https://www.youtube.com/watch?v=kCc8FmEb1nY",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/kCc8FmEb1nY/hqdefault.jpg",
            DurationSeconds = 50,
            PublishedAt = DateTime.UtcNow,
        }
    ];

    private static List<Clip> GetDataAnalyticsClips(string userId) =>
    [
        new Clip
        {
            UserId = userId,
            Tag = "Data Analytics",
            Signal = "SQL Window Functions in 60 Seconds: RANK vs DENSE_RANK vs ROW_NUMBER",
            WhyItMatters = "Window functions allow complex analytical aggregations without collapsing row context, saving hundreds of lines of self-joins and subqueries.",
            YourEdge = "Mastering window partition clauses allows you to compute running totals, cohort retention rates, and moving averages directly in the database engine.",
            NextMove = "Write a query calculating month-over-month revenue growth using LAG(revenue, 1) OVER (ORDER BY month).",
            Source = "Alex The Analyst · SQL Mastery",
            MediaUrl = "https://www.youtube.com/watch?v=PSNXoAs28x4",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/PSNXoAs28x4/hqdefault.jpg",
            DurationSeconds = 45,
            PublishedAt = DateTime.UtcNow,
        },
        new Clip
        {
            UserId = userId,
            Tag = "Python & BI",
            Signal = "Speeding up Pandas DataFrames by 50x with Vectorization instead of apply()",
            WhyItMatters = "Row-by-row iteration in Python bottlenecks data pipelines; numpy vectorization processes columns in C-level memory buffers.",
            YourEdge = "You can process multimillion-row datasets locally without crashing memory or needing distributed cluster overhead.",
            NextMove = "Refactor a DataFrame `.apply(lambda x: ...)` expression into direct vectorized array operations.",
            Source = "Luke Barousse · Data Science",
            MediaUrl = "https://www.youtube.com/watch?v=r-uOLxNrNk8",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/r-uOLxNrNk8/hqdefault.jpg",
            DurationSeconds = 50,
            PublishedAt = DateTime.UtcNow,
        }
    ];

    private static List<Clip> GetAiAndTechClips(string userId) =>
    [
        new Clip
        {
            UserId = userId,
            Tag = "AI Architecture",
            Signal = "How Vector Databases & Embeddings power Retrieval-Augmented Generation (RAG)",
            WhyItMatters = "LLMs hallucinate without grounded context; vector cosine similarity allows finding the top-k relevant documentation chunks in milliseconds.",
            YourEdge = "You can build real-time enterprise search tools that augment AI responses with accurate company documentation.",
            NextMove = "Explore HNSW indexing mechanics and chunk overlap strategies for document ingestion.",
            Source = "Fireship · 100 Seconds of Code",
            MediaUrl = "https://www.youtube.com/watch?v=IF526m1hZxE",
            MediaType = "video",
            ThumbnailUrl = "https://img.youtube.com/vi/IF526m1hZxE/hqdefault.jpg",
            DurationSeconds = 60,
            PublishedAt = DateTime.UtcNow,
        }
    ];

    private async Task PersonalizeClipsWithAiAsync(List<Clip> clips, UserProfile profile, GrowthRoadmap? activeRoadmap)
    {
        if (clips.Count == 0) return;
        try
        {
            var summaries = string.Join("\n\n", clips.Take(4).Select((c, idx) =>
                $"Clip {idx + 1}:\nTitle: {c.Signal}\nTag: {c.Tag}\nCurrentWhy: {c.WhyItMatters}"));

            var prompt = $$"""
                User Profile:
                - Goal: {{profile.PrimaryGoal}}
                - Role: {{profile.TargetRole}}
                - Roadmap Topic: {{activeRoadmap?.Interest ?? profile.PrimaryGoal}}
                - Target Industry: {{profile.TargetIndustry}}
                - Location: {{profile.City}}, {{profile.Region}}

                Clips to personalize:
                {{summaries}}

                For each clip, personalize 'whyItMatters', 'yourEdge', and 'nextMove' to specifically match this user's stated goal, role, and active learning roadmap.
                Return strict JSON:
                {
                  "personalizedClips": [
                    {
                      "index": 1,
                      "whyItMatters": "...",
                      "yourEdge": "...",
                      "nextMove": "..."
                    }
                  ]
                }
                """;

            var result = await _ai.GenerateJsonAsync<ClipPersonalizationResult>(
                "clips-personalization",
                "You are Radar's AI Clip Intelligence Processor. Transform video clips into hyper-relevant action intelligence.",
                prompt);

            if (result?.PersonalizedClips is not null)
            {
                foreach (var item in result.PersonalizedClips)
                {
                    var idx = item.Index - 1;
                    if (idx >= 0 && idx < clips.Count)
                    {
                        if (!string.IsNullOrWhiteSpace(item.WhyItMatters)) clips[idx].WhyItMatters = item.WhyItMatters;
                        if (!string.IsNullOrWhiteSpace(item.YourEdge)) clips[idx].YourEdge = item.YourEdge;
                        if (!string.IsNullOrWhiteSpace(item.NextMove)) clips[idx].NextMove = item.NextMove;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Non-fatal: AI Engine clip personalization failed; keeping baseline insights");
        }
    }

    private sealed class ClipPersonalizationResult
    {
        public List<PersonalizedClipItem>? PersonalizedClips { get; set; }
    }

    private sealed class PersonalizedClipItem
    {
        public int Index { get; set; }
        public string? WhyItMatters { get; set; }
        public string? YourEdge { get; set; }
        public string? NextMove { get; set; }
    }
}
