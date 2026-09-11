using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Data.Documents;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Manages subscription plans. Plans are seeded once; current plan per-user is persisted.
/// </summary>
public class MongoPlansService : IPlansService
{
    private readonly RadarDatabase _db;

    public MongoPlansService(RadarDatabase db) => _db = db;

    public Task<List<SubscriptionPlan>> GetPlansAsync()
    {
        // Plans are static config — return directly
        return Task.FromResult(Plans);
    }

    public async Task<SubscriptionPlan?> GetCurrentPlanAsync(string userId)
    {
        // Check if user has a subscription record
        var userPlan = await _db.UserSubscriptions
            .Find(s => s.UserId == userId)
            .FirstOrDefaultAsync();

        var planId = userPlan?.PlanId ?? "free";
        return Plans.FirstOrDefault(p => p.Id == planId);
    }

    private static readonly List<SubscriptionPlan> Plans =
    [
        new SubscriptionPlan
        {
            Id = "free",
            Name = "Free",
            IsCurrent = true,
            IsPro = false,
            MonthlyPrice = 0,
            YearlyPrice = 0,
            Features =
            [
                "Weekly Intelligence Brief",
                "Full Intelligence Feed — articles, podcasts, videos, papers",
                "Your Growth Roadmap and Learn Hub",
                "Saved resources · up to 100 items",
                "Ask Radar · 10 questions a day",
                "Opportunity Hub with match scores",
            ],
            Note = "your current plan",
        },
        new SubscriptionPlan
        {
            Id = "pro",
            Name = "Radar Pro",
            IsCurrent = false,
            IsPro = true,
            MonthlyPrice = 3000,
            YearlyPrice = 25200,
            Currency = "₦",
            Badge = "Recommended",
            Features =
            [
                "Unlimited Ask Radar",
                "AI Learning Mentor · unlimited sessions",
                "Unlimited saved resources",
                "Research Discovery across all five databases",
                "Offline downloads for reading and video",
                "Project Studio · all templates with AI review",
            ],
        },
    ];
}

