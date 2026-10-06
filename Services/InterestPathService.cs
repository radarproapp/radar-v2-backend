using RadarV2.Models;

namespace RadarV2.Services;

public static class InterestPathService
{
    private static readonly string[][] Families =
    [
        ["artificial intelligence", "ai", "machine learning", "data analytics", "data science", "finance", "fintech"],
        ["technology", "software", "product management", "entrepreneurship", "marketing", "business"],
        ["health", "medicine", "clinical psychology", "biology", "science"],
        ["energy", "petroleum engineering", "environment", "agriculture", "engineering"],
        ["fashion", "art", "design", "literature", "film", "music"],
        ["football", "sports"]
    ];

    public static List<InterestPath> Build(UserProfile profile)
    {
        var interests = profile.Interests.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (interests.Count == 0) return [];

        var groups = new List<List<string>>();
        foreach (var interest in interests)
        {
            var group = groups.FirstOrDefault(existing => existing.Any(item => Related(item, interest)));
            if (group is null) groups.Add([interest]);
            else group.Add(interest);
        }

        var dominant = profile.DominantInterests.Count > 0
            ? profile.DominantInterests
            : interests.Take(1).ToList();

        return groups.Select(group =>
        {
            var isPrimary = group.Any(i => dominant.Contains(i, StringComparer.OrdinalIgnoreCase));
            var strength = group.Count == 1 ? 0 : Math.Min(100, 45 + (group.Count - 2) * 20);
            return new InterestPath
            {
                Title = IntersectionTitle(group),
                Interests = group,
                RelationshipStrength = strength,
                IsPrimary = isPrimary,
                Priority = isPrimary ? "Primary focus" : group.Any(i => profile.DominantInterests.Contains(i, StringComparer.OrdinalIgnoreCase)) ? "Secondary focus" : "Explore occasionally"
            };
        }).OrderByDescending(path => path.IsPrimary).ThenByDescending(path => path.Interests.Count).ToList();
    }

    public static IReadOnlyList<string> ActiveInterests(UserProfile profile) =>
        (profile.InterestPaths.Count > 0 ? profile.InterestPaths.Where(p => p.IsPrimary).SelectMany(p => p.Interests) : profile.DominantInterests)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool Related(string left, string right)
    {
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return true;
        return Families.Any(family => family.Any(term => Matches(term, left)) && family.Any(term => Matches(term, right)));
    }

    private static bool Matches(string term, string value)
    {
        // Short terms ("ai") must match on a word boundary — otherwise "ai" hits "retail",
        // "email", "chain", and interests get grouped into the wrong family.
        if (term.Length <= 3)
            return Tokenize(value).Contains(term, StringComparer.OrdinalIgnoreCase);

        return value.Contains(term, StringComparison.OrdinalIgnoreCase) || term.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Tokenize(string value) =>
        value.Split([' ', '&', ',', '/', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Names the intersection of a path's interests. A curated map covers the combinations the
    /// product cares about (AI×Finance, Climate×Finance, …); otherwise the interests are joined.
    /// </summary>
    private static string IntersectionTitle(List<string> group)
    {
        if (group.Count == 1) return group[0];

        var names = group.Select(i => i.ToLowerInvariant()).ToList();
        bool Has(params string[] keys) => keys.Any(k => names.Any(n => n.Contains(k)));

        if (Has("artificial intelligence", "ai", "machine learning") && Has("finance", "fintech", "banking", "invest"))
            return "AI & Financial Markets";
        if (Has("climate", "environment", "sustainab", "carbon") && Has("finance", "fintech", "invest", "banking"))
            return "Climate Finance & Sustainable Investment";
        if (Has("climate", "environment", "energy", "renewable") && Has("policy", "governance", "regulat"))
            return "Climate Policy & Regulation";
        if (Has("artificial intelligence", "ai", "machine learning") && Has("policy", "governance", "regulat", "law"))
            return "AI Governance & Regulation";
        if (Has("artificial intelligence", "ai", "machine learning") && Has("health", "medicine", "clinical"))
            return "AI in Health";
        if (Has("data", "analytics", "statistic") && Has("finance", "fintech", "banking"))
            return "Financial Analytics";
        if (Has("entrepreneur", "startup", "business") && Has("technology", "software", "product management"))
            return "Technology Entrepreneurship";
        if (Has("africa", "nigeria", "ghana", "kenya") && Has("finance", "fintech", "invest"))
            return "African Finance & Fintech";

        return string.Join(" & ", group.Take(3).Select(Display));
    }

    private static string Display(string interest) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(interest.ToLowerInvariant());
}
