using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public sealed class AiOpportunityMatchService
{
    private readonly IAiEngine _ai;

    public AiOpportunityMatchService(IAiEngine ai) => _ai = ai;

    public async Task ApplyAsync(UserProfile profile, IList<Opportunity> opportunities, CancellationToken ct = default)
    {
        if (opportunities.Count == 0) return;
        var candidates = opportunities.Take(30).Select(o => new { id = o.Id, title = o.Title, type = o.Type.ToString(), organisation = o.Organisation, description = o.Description, requirements = o.Requirements, location = o.Location });
        var prompt = $@"Profile:
persona: {profile.Persona}
goal: {profile.PrimaryGoal}
interests: {string.Join(", ", profile.Interests)}
capabilities: {string.Join(", ", profile.InterestContexts.Select(c => c.Lens).Where(x => !string.IsNullOrWhiteSpace(x)))}
region: {profile.City}, {profile.Region}

Opportunities:
{System.Text.Json.JsonSerializer.Serialize(candidates)}

For each opportunity, estimate fit using the stated goal and current situation. Do not reward generic interest matches over direct goal alignment. Return strict JSON: {{ ""matches"": [{{""id"":""..."",""score"":0,""confidence"":0,""whyItFits"":""..."",""preparationSteps"":[]}}] }}";
        var result = await _ai.GenerateJsonAsync<MatchResult>("opportunity-matching", "You are Radar's opportunity matching engine. Be conservative and never invent eligibility facts.", prompt, ct);
        if (result?.Matches is null) return;
        var byId = result.Matches.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var opportunity in opportunities)
        {
            if (!byId.TryGetValue(opportunity.Id, out var match)) continue;
            opportunity.MatchScorePercent = Math.Clamp((int)Math.Round(match.Score * 100), 0, 99);
            opportunity.MatchConfidence = Math.Clamp(match.Confidence, 0, 1);
            opportunity.WhyItFits = match.WhyItFits;
            opportunity.PreparationSteps = match.PreparationSteps ?? [];
        }
        var ranked = opportunities.OrderByDescending(o => o.MatchScorePercent).ToList();
        for (var i = 0; i < ranked.Count; i++) opportunities[i] = ranked[i];
    }

    private sealed class MatchResult { public List<Match> Matches { get; set; } = []; }
    private sealed class Match
    {
        public string Id { get; set; } = string.Empty;
        public double Score { get; set; }
        public double Confidence { get; set; }
        public string? WhyItFits { get; set; }
        public List<string>? PreparationSteps { get; set; }
    }
}
