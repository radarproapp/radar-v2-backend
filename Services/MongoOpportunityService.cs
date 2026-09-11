using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public class MongoOpportunityService : IOpportunityService
{
    private readonly RadarDatabase _db;
    private static bool _seeded;
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

    public MongoOpportunityService(RadarDatabase db) => _db = db;

    public async Task<List<Opportunity>> GetOpportunitiesAsync(
        UserProfile profile, OpportunityType? filterType = null, int page = 1, int pageSize = 20)
    {
        await EnsureSeedAsync();

        var filter = filterType.HasValue
            ? Builders<Opportunity>.Filter.Eq(o => o.Type, filterType.Value)
            : Builders<Opportunity>.Filter.Empty;

        var all = await _db.Opportunities
            .Find(filter)
            .SortByDescending(o => o.Deadline)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        // Apply personalised match scoring and mark saved/applied state
        var savedIds   = await GetSavedIdsAsync(profile.Id);
        var appliedIds = await GetAppliedIdsAsync(profile.Id);

        foreach (var opp in all)
        {
            opp.MatchScorePercent = ComputeMatchScore(opp, profile);
            opp.IsSaved           = savedIds.Contains(opp.Id);
        }

        // Sort by match score descending after personalisation
        all.Sort((a, b) => b.MatchScorePercent.CompareTo(a.MatchScorePercent));
        return all;
    }

    public async Task<Opportunity?> GetByIdAsync(string id)
    {
        await EnsureSeedAsync();
        return await _db.Opportunities.Find(o => o.Id == id).FirstOrDefaultAsync();
    }

    public async Task SaveOpportunityAsync(string userId, string opportunityId)
    {
        var exists = await _db.SavedOpportunities
            .Find(s => s.UserId == userId && s.OpportunityId == opportunityId)
            .AnyAsync();
        if (!exists)
            await _db.SavedOpportunities.InsertOneAsync(new SavedOpportunityDoc
            {
                UserId = userId, OpportunityId = opportunityId
            });
    }

    public async Task UnsaveOpportunityAsync(string userId, string opportunityId)
    {
        await _db.SavedOpportunities.DeleteOneAsync(
            s => s.UserId == userId && s.OpportunityId == opportunityId);
    }

    public async Task<List<Opportunity>> GetSavedOpportunitiesAsync(string userId)
    {
        await EnsureSeedAsync();
        var savedIds = await GetSavedIdsAsync(userId);
        if (savedIds.Count == 0) return [];

        var items = await _db.Opportunities
            .Find(Builders<Opportunity>.Filter.In(o => o.Id, savedIds))
            .ToListAsync();
        foreach (var o in items) o.IsSaved = true;
        return items;
    }

    public async Task MarkAppliedAsync(string userId, string opportunityId)
    {
        var exists = await _db.AppliedOpportunities
            .Find(a => a.UserId == userId && a.OpportunityId == opportunityId)
            .AnyAsync();
        if (!exists)
            await _db.AppliedOpportunities.InsertOneAsync(new AppliedOpportunityDoc
            {
                UserId = userId, OpportunityId = opportunityId
            });
    }

    // ── Match scoring ─────────────────────────────────────────────────────────

    private static int ComputeMatchScore(Opportunity opp, UserProfile profile)
    {
        int score = 45; // base

        // Interest keyword overlap with title + description
        var text = $"{opp.Title} {opp.Description} {opp.Organisation}".ToLowerInvariant();
        var goalText = profile.PrimaryGoal.ToLowerInvariant();

        foreach (var interest in profile.Interests)
        {
            var norm = interest.ToLowerInvariant();
            if (text.Contains(norm) || InterestMatches(norm, text))
                score += 10;
        }

        // Goal alignment
        foreach (var goalWord in goalText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (goalWord.Length > 3 && text.Contains(goalWord))
                score += 8;

        // Persona bonus
        score += profile.Persona switch
        {
            PersonaType.YoungProfessional when opp.Type is OpportunityType.Internship or OpportunityType.Scholarship => 8,
            PersonaType.YoungProfessional when opp.Type is OpportunityType.Fellowship                                => 5,
            _ => 0
        };

        // Deadline bonus — sooner deadlines are more urgent and feel more relevant
        score += opp.DaysUntilDeadline switch
        {
            <= 7  => 5,
            <= 21 => 3,
            _     => 0
        };

        return Math.Min(score, 95);
    }

    private static bool InterestMatches(string interest, string text) => interest switch
    {
        var s when s.Contains("tech") || s.Contains("ai") || s.Contains("software")
            => text.Contains("technolog") || text.Contains("digital") || text.Contains("data") || text.Contains("software"),
        var s when s.Contains("business") || s.Contains("entrepreneur")
            => text.Contains("business") || text.Contains("entrepreneur") || text.Contains("startup") || text.Contains("enterprise"),
        var s when s.Contains("finance") || s.Contains("invest")
            => text.Contains("financ") || text.Contains("invest") || text.Contains("economic") || text.Contains("bank"),
        var s when s.Contains("policy") || s.Contains("governance")
            => text.Contains("polic") || text.Contains("govern") || text.Contains("public sector"),
        var s when s.Contains("health") || s.Contains("medicine")
            => text.Contains("health") || text.Contains("medic") || text.Contains("biomed"),
        var s when s.Contains("science") || s.Contains("research")
            => text.Contains("research") || text.Contains("science") || text.Contains("academic"),
        _ => false
    };

    // ── Saved / Applied helpers ───────────────────────────────────────────────

    private async Task<HashSet<string>> GetSavedIdsAsync(string userId)
    {
        var docs = await _db.SavedOpportunities
            .Find(s => s.UserId == userId).ToListAsync();
        return docs.Select(s => s.OpportunityId).ToHashSet();
    }

    private async Task<HashSet<string>> GetAppliedIdsAsync(string userId)
    {
        var docs = await _db.AppliedOpportunities
            .Find(a => a.UserId == userId).ToListAsync();
        return docs.Select(a => a.OpportunityId).ToHashSet();
    }

    // ── Seeding ───────────────────────────────────────────────────────────────

    private async Task EnsureSeedAsync()
    {
        if (_seeded) return;
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded) return;
            var count = await _db.Opportunities.CountDocumentsAsync(Builders<Opportunity>.Filter.Empty);
            if (count == 0)
                await _db.Opportunities.InsertManyAsync(SeedData);
            _seeded = true;
        }
        finally { _seedLock.Release(); }
    }

    private static readonly List<Opportunity> SeedData =
    [
        new()
        {
            Id = "opp-mcf-scholarship",
            Type = OpportunityType.Scholarship,
            Title = "MasterCard Foundation Scholars Program",
            Organisation = "MasterCard Foundation",
            Description = "Full scholarship for academically talented yet financially disadvantaged young Africans to study at partner universities globally. Covers tuition, accommodation, living expenses and return flights. Scholars join a lifelong network of African leaders.",
            Deadline = DateTime.UtcNow.AddMonths(2),
            Requirements = ["African nationality", "Undergraduate or postgraduate applicant", "Demonstrated financial need", "Academic excellence (top 10% of class)", "Leadership and community involvement"],
            MatchScorePercent = 70,
            Url = "#",
            IsRemote = false,
            Location = "Various — Partner Universities Worldwide"
        },
        new()
        {
            Id = "opp-schwarzman",
            Type = OpportunityType.Fellowship,
            Title = "Schwarzman Scholars Program",
            Organisation = "Schwarzman College, Tsinghua University",
            Description = "One-year master's program designed to prepare the next generation of global leaders through deep engagement with China. Covers all costs including flights, accommodation, and a personal stipend.",
            Deadline = DateTime.UtcNow.AddMonths(3),
            Requirements = ["Bachelor's degree", "Under 29 years old", "Evidence of leadership", "English proficiency", "Academic excellence"],
            MatchScorePercent = 65,
            Url = "#",
            IsRemote = false,
            Location = "Beijing, China"
        },
        new()
        {
            Id = "opp-chevening",
            Type = OpportunityType.Scholarship,
            Title = "Chevening Scholarship",
            Organisation = "UK Foreign, Commonwealth & Development Office",
            Description = "Fully funded UK government scholarship for emerging leaders from around the world. Covers a one-year master's degree at any UK university of your choice, plus flights and a living allowance.",
            Deadline = DateTime.UtcNow.AddMonths(4),
            Requirements = ["Citizen of a Chevening-eligible country", "2+ years work experience", "Bachelor's degree", "Leadership potential", "Return to home country for 2 years after study"],
            MatchScorePercent = 72,
            Url = "#",
            IsRemote = false,
            Location = "United Kingdom"
        },
        new()
        {
            Id = "opp-tony-elumelu",
            Type = OpportunityType.ResearchGrant,
            Title = "Tony Elumelu Foundation Entrepreneurship Program",
            Organisation = "Tony Elumelu Foundation",
            Description = "Africa's largest entrepreneurship program — USD 5,000 seed capital, 12 weeks of training, and mentorship for 1,000 African entrepreneurs annually. Open to early-stage entrepreneurs across all 54 African countries.",
            Deadline = DateTime.UtcNow.AddMonths(1),
            Requirements = ["African nationality", "Early-stage business (0–3 years)", "Registered business or business idea", "Full-time commitment to business"],
            MatchScorePercent = 68,
            Url = "#",
            IsRemote = true,
            Location = "Pan-African (Remote training)"
        },
        new()
        {
            Id = "opp-mandela-rhodes",
            Type = OpportunityType.Scholarship,
            Title = "Mandela Rhodes Scholarship",
            Organisation = "Mandela Rhodes Foundation",
            Description = "Fully funded postgraduate scholarship at South African universities, building exceptional leadership in Africa. Combines financial support with a structured leadership development programme.",
            Deadline = DateTime.UtcNow.AddMonths(5),
            Requirements = ["African citizen", "Under 30 years old", "Outstanding academic record", "Evidence of community leadership", "South African university postgrad application"],
            MatchScorePercent = 71,
            Url = "#",
            IsRemote = false,
            Location = "South Africa"
        },
        new()
        {
            Id = "opp-google-pm-intern",
            Type = OpportunityType.Internship,
            Title = "Product Manager Internship",
            Organisation = "Google",
            Description = "12-week internship on Google's product team working on consumer products used by billions. Interns own a project end-to-end, collaborating with engineering, design, and business. Competitive stipend and relocation support.",
            Deadline = DateTime.UtcNow.AddDays(21),
            Requirements = ["Final-year student or recent graduate", "Degree in Computer Science, Business, or related field", "Strong analytical and problem-solving skills", "Experience with user research or data analysis"],
            MatchScorePercent = 79,
            Url = "#",
            IsRemote = false,
            Location = "London, UK / Dublin, Ireland"
        },
        new()
        {
            Id = "opp-microsoft-africa-research",
            Type = OpportunityType.Internship,
            Title = "Research Intern — Africa",
            Organisation = "Microsoft Research Africa",
            Description = "Research internship at Microsoft's Nairobi lab working on applied ML, computational social science, or systems for the next billion users. Work alongside world-class researchers on publishable projects.",
            Deadline = DateTime.UtcNow.AddMonths(2),
            Requirements = ["Enrolled in graduate program (PhD or Masters)", "Background in machine learning, HCI, or systems", "Research experience or publications preferred", "Strong programming skills"],
            MatchScorePercent = 73,
            Url = "#",
            IsRemote = false,
            Location = "Nairobi, Kenya"
        },
        new()
        {
            Id = "opp-stripe-data-analyst",
            Type = OpportunityType.RemoteJob,
            Title = "Junior Data Analyst — EMEA",
            Organisation = "Stripe",
            Description = "Fully remote data analyst role supporting growth analytics across EMEA markets. You will build dashboards, analyse payment flows, and surface insights to product and business teams. Strong Python/SQL required.",
            Deadline = DateTime.UtcNow.AddDays(28),
            Requirements = ["Python or R proficiency", "SQL intermediate-advanced", "1+ year experience or strong portfolio", "Experience with dbt or Looker a plus", "Self-directed working style"],
            MatchScorePercent = 77,
            Url = "#",
            IsRemote = true,
            Location = "Remote — EMEA"
        },
        new()
        {
            Id = "opp-ycombinator-startup",
            Type = OpportunityType.ResearchGrant,
            Title = "Y Combinator Startup Program",
            Organisation = "Y Combinator",
            Description = "The world's most successful startup accelerator. $500K investment for ~7% equity. 3-month program in San Francisco with weekly dinners, office hours with YC partners and alumni, and access to the YC network of 10,000+ founders.",
            Deadline = DateTime.UtcNow.AddMonths(3),
            Requirements = ["Technical co-founder or strong technical ability", "Working prototype or MVP", "Team of 1–4 founders", "Full-time commitment during batch"],
            MatchScorePercent = 60,
            Url = "#",
            IsRemote = false,
            Location = "San Francisco, CA"
        },
        new()
        {
            Id = "opp-daad-scholarship",
            Type = OpportunityType.Scholarship,
            Title = "DAAD Development-Related Postgraduate Courses",
            Organisation = "DAAD (German Academic Exchange Service)",
            Description = "Fully funded postgraduate scholarships for professionals from developing countries to study in Germany. Focus areas include agriculture, engineering, economics, social sciences, and natural sciences.",
            Deadline = DateTime.UtcNow.AddMonths(6),
            Requirements = ["Citizen of a developing country", "Bachelor's degree with above-average results", "2+ years professional experience", "Under 36 years old at time of application", "English or German proficiency"],
            MatchScorePercent = 66,
            Url = "#",
            IsRemote = false,
            Location = "Germany"
        },
        new()
        {
            Id = "opp-bcg-nigeria-intern",
            Type = OpportunityType.Internship,
            Title = "Business Analyst Internship",
            Organisation = "Boston Consulting Group — Lagos",
            Description = "Summer internship in BCG's Lagos office working on strategy and management consulting projects across West Africa. Analysts work on high-impact engagements spanning finance, energy, consumer, and public sector.",
            Deadline = DateTime.UtcNow.AddDays(45),
            Requirements = ["Penultimate year undergraduate or postgraduate", "Strong academic record", "Analytical and problem-solving orientation", "Business, Economics, or STEM background preferred"],
            MatchScorePercent = 74,
            Url = "#",
            IsRemote = false,
            Location = "Lagos, Nigeria"
        },
        new()
        {
            Id = "opp-stanford-knight-hennessy",
            Type = OpportunityType.Scholarship,
            Title = "Knight-Hennessy Scholars Program",
            Organisation = "Stanford University",
            Description = "Fully funded graduate fellowship at Stanford for future global leaders. Covers tuition, a living stipend, and travel grants across all Stanford graduate programs. Cohort of up to 100 scholars per year.",
            Deadline = DateTime.UtcNow.AddMonths(4),
            Requirements = ["Bachelor's degree", "Apply to a Stanford graduate program simultaneously", "Demonstrated leadership impact", "Civic orientation and collaborative mindset"],
            MatchScorePercent = 63,
            Url = "#",
            IsRemote = false,
            Location = "Stanford, California"
        },
    ];
}
