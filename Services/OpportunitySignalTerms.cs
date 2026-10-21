using RadarV2.Models;

namespace RadarV2.Services;

/// <summary>
/// The handful of words worth generalising from one opportunity.
///
/// When a user marks something interesting or not relevant, the question is what Radar should
/// learn. Deliberately short, and deliberately not the title: "Google Cybersecurity Professional
/// Fellowship" identifies this one opportunity and transfers to nothing. Learning every word of
/// the description is the opposite failure — it teaches noise, and noise outvotes signal once
/// there is enough of it.
///
/// The tags are what another opportunity can actually share.
/// </summary>
public static class OpportunitySignalTerms
{
    public static IReadOnlyList<string> For(Opportunity opportunity)
    {
        var terms = new List<string> { opportunity.Type.ToString() };

        if (!string.IsNullOrWhiteSpace(opportunity.GoalTag)) terms.Add(opportunity.GoalTag.Trim());

        // The field this opportunity is in, when it names one Radar knows. Returns nothing rather
        // than guessing, so an unlisted field is never taught as some other field — the mistake
        // that sent social-work learners cybersecurity results.
        var field = DomainVocabulary.FieldOf($"{opportunity.Title} {opportunity.Description}");
        if (field is not null) terms.Add(field);

        return terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
