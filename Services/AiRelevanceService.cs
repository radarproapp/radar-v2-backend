using System.Text.Json.Serialization;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

public sealed class AiRelevanceService
{
    private readonly IAiEngine _ai;
    private readonly IBehavioralSignalService _signals;

    public AiRelevanceService(IAiEngine ai, IBehavioralSignalService signals)
    {
        _ai = ai;
        _signals = signals;
    }

    public async Task ApplyAsync(UserProfile profile, IList<ContentItem> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        var topSignals = await _signals.GetTopSignalsAsync(profile.Id, 10);
        var signalContext = string.Join(", ", topSignals.Select(s => $"{s.Term} ({(s.Source == SignalSource.Declared ? "stated" : "inferred")}, {BehavioralSignalService.DecayedStrength(s):0.00})"));
        var candidates = items.Take(30).Select(item => new
        {
            id = item.Id,
            title = item.Title,
            topic = item.Topic,
            tags = item.Tags,
            summary = item.WhatHappened,
        });
        var request = $@"User profile:
persona: {profile.Persona}
goal: {profile.PrimaryGoal}
interests: {string.Join(", ", profile.Interests)}
dominant interests: {string.Join(", ", profile.DominantInterests)}
context: {string.Join("; ", profile.InterestContexts.Select(c => $"{c.Interest}: {c.Goal}; {c.Lens}"))}
region: {profile.City}, {profile.Region}
behavioural signals (recency-decayed): {signalContext}

Candidates:
{System.Text.Json.JsonSerializer.Serialize(candidates)}

Rank candidates for this person. Goal and current context must outrank generic interest matches.
Do not invent facts. Return strict JSON with this shape:
{{ ""rankings"": [{{""id"":""..."",""score"":0.0,""confidence"":0.0,""matchedSignals"":[],""whyShown"":""..."",""nextMove"":""...""}}] }}";
        var result = await _ai.GenerateJsonAsync<RelevanceResult>("feed-relevance", "You are Radar's relevance ranking engine. Be conservative, specific and evidence-based.", request, ct);
        if (result?.Rankings is null) return;
        var byId = result.Rankings.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!byId.TryGetValue(item.Id, out var rank)) continue;
            item.RelevanceScore = Math.Clamp(rank.Score, 0, 1);
            item.RelevanceConfidence = Math.Clamp(rank.Confidence, 0, 1);
            item.MatchedSignals = rank.MatchedSignals ?? [];
            item.PersonalizedWhy = rank.WhyShown;
            item.NextMove = rank.NextMove;
        }
        var ranked = items.OrderByDescending(i => i.RelevanceScore).ToList();
        for (var i = 0; i < ranked.Count; i++) items[i] = ranked[i];
    }

    private sealed class RelevanceResult { public List<Ranking> Rankings { get; set; } = []; }
    private sealed class Ranking
    {
        public string Id { get; set; } = string.Empty;
        public double Score { get; set; }
        public double Confidence { get; set; }
        public List<string>? MatchedSignals { get; set; }
        public string? WhyShown { get; set; }
        public string? NextMove { get; set; }
    }
}
