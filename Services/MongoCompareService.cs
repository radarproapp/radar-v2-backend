using System.Text;
using System.Text.Json;
using MongoDB.Driver;
using RadarV2.Data;
using RadarV2.Models;
using RadarV2.Services.Interfaces;

namespace RadarV2.Services;

/// <summary>
/// Persists policy comparisons per-user and generates new ones via OpenRouter LLM.
/// </summary>
public class MongoCompareService : ICompareService
{
    private readonly RadarDatabase _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly IAiEngine _ai;

    public MongoCompareService(RadarDatabase db, IHttpClientFactory httpFactory, IConfiguration config, IAiEngine ai)
    {
        _db = db;
        _httpFactory = httpFactory;
        _config = config;
        _ai = ai;
    }

    public async Task<PolicyComparison?> GetComparisonAsync(string comparisonId)
    {
        return await _db.Comparisons
            .Find(c => c.Id == comparisonId)
            .FirstOrDefaultAsync();
    }

    public async Task<List<PolicyComparison>> GetComparisonsAsync(string userId)
    {
        return await _db.Comparisons
            .Find(c => c.UserId == userId)
            .SortByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<PolicyComparison> CreateComparisonAsync(string userId, List<string> itemIds)
    {
        var prompt = $"Create a side-by-side policy comparison between these items: {string.Join(" vs ", itemIds)}. " +
            "Return a JSON object with this exact structure: { \"title\": \"Comparison title\", \"summary\": \"One-paragraph summary\", \"whyItMatters\": \"Why this matters\", \"aiEdge\": \"Strategic insight\", \"rows\": [ { \"label\": \"Row\", \"valueA\": \"A\", \"valueB\": \"B\", \"tag\": \"Same|Differs|Context\" } ], \"disclaimer\": \"Disclaimer\" } " +
            "Include 5-7 rows covering scope, timeline, penalties, requirements, enforcement. Use Same/Differs/Context tags.";

        var llmResponse = await CallLlmAsync(prompt);

        var comparison = new PolicyComparison
        {
            UserId = userId,
            Items = itemIds,
            CreatedAt = DateTime.UtcNow,
        };

        // Try to parse LLM response as JSON
        try
        {
            using var doc = JsonDocument.Parse(llmResponse);
            var root = doc.RootElement;

            comparison.Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Comparison" : "Comparison";
            comparison.Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            comparison.WhyItMatters = root.TryGetProperty("whyItMatters", out var w) ? w.GetString() ?? "" : "";
            comparison.AiEdge = root.TryGetProperty("aiEdge", out var a) ? a.GetString() : null;
            comparison.Disclaimer = root.TryGetProperty("disclaimer", out var d) ? d.GetString() : null;

            if (root.TryGetProperty("rows", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    comparison.Rows.Add(new ComparisonRow
                    {
                        Label = row.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                        ValueA = row.TryGetProperty("valueA", out var va) ? va.GetString() ?? "" : "",
                        ValueB = row.TryGetProperty("valueB", out var vb) ? vb.GetString() ?? "" : "",
                        Tag = row.TryGetProperty("tag", out var tag) && tag.GetString() == "Differs"
                            ? ComparisonRowTag.Differs
                            : row.TryGetProperty("tag", out var tag2) && tag2.GetString() == "Context"
                                ? ComparisonRowTag.Context
                                : ComparisonRowTag.Same,
                    });
                }
            }
        }
        catch
        {
            // Fallback if JSON parsing fails
            comparison.Title = $"Comparison: {string.Join(" vs ", itemIds)}";
            comparison.Summary = llmResponse;
        }

        await _db.Comparisons.InsertOneAsync(comparison);
        return comparison;
    }

    private async Task<string> CallLlmAsync(string prompt)
    {
        return await _ai.GenerateTextAsync("policy-comparison", "You are Radar's policy analyst. Return only valid JSON, with no markdown code fences.", prompt) ?? "{}";
    }
}
