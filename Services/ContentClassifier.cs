using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// Lightweight, deterministic content→domain classifier used when no AI enrichment is available.
/// Scans an item's own text for high-signal domain words and maps the first one that hits to a
/// <see cref="ContentLayer"/>. This is what stops a general outlet's layer ("Law") from being the
/// displayed category for every article it publishes.
/// </summary>
public static class ContentClassifier
{
    // Ordered by specificity/priority: the first domain word present wins.
    private static readonly string[] DomainTokens =
    [
        // Finance / markets
        "finance", "financial", "fintech", "banking", "bank", "investment", "investor", "stock",
        "market", "tax", "insurance", "portfolio", "valuation", "accounting", "economy", "economic",
        // Climate / environment / energy
        "climate", "environment", "sustainab", "carbon", "renewable", "energy", "oil", "gas", "power",
        // Law / policy / governance
        "law", "legal", "court", "regulat", "policy", "governance", "politic", "election",
        // Technology / science
        "artificial intelligence", "machine learning", "technology", "software", "cyber", "cloud",
        "data", "robot", "science", "research", "academic", "statistic",
        // Health
        "health", "medical", "medicine", "hospital", "disease",
        // Business / career
        "business", "startup", "entrepreneur", "marketing", "management", "leadership", "career",
        "job", "employment", "hiring",
        // Education
        "education", "school", "university", "learning", "student",
        // Agriculture / industry / transport
        "agriculture", "farm", "food", "mining", "manufacturing", "industry", "logistics", "transport",
        // Lifestyle / culture
        "sport", "music", "film", "cinema", "travel", "property", "real estate", "history",
        "religion", "faith", "fashion",
    ];

    public static ContentLayer? DetectLayer(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var token in DomainTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                var layer = TopicLayerMapper.Map([token]);
                if (layer is not null) return layer;
            }
        }
        return null;
    }

    /// <summary>Convenience overload for an item (title + summary + signal).</summary>
    public static ContentLayer? DetectLayer(ContentItem item) =>
        DetectLayer($"{item.Title} {item.Signal} {item.AiSummary} {item.WhatHappened}");
}
