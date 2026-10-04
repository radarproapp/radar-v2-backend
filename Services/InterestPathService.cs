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

    private static bool Matches(string term, string value) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase) || term.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string IntersectionTitle(List<string> group)
    {
        if (group.Count == 1) return group[0];
        var names = group.Select(i => i.ToLowerInvariant()).ToList();
        if (names.Any(i => i.Contains("artificial intelligence") || i == "ai") && names.Any(i => i.Contains("finance")) && names.Any(i => i.Contains("data"))) return "AI & Financial Analytics";
        if (names.Any(i => i.Contains("entrepreneur")) && names.Any(i => i.Contains("technology")) && names.Any(i => i.Contains("marketing"))) return "Technology Entrepreneurship & Growth";
        return string.Join(" & ", group.Take(3));
    }
}
