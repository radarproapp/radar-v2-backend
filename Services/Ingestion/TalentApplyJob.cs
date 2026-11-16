using System.Text.Json;

namespace RadarV2.Services.Ingestion;

/// <summary>
/// One job listing from the partner API, read field by field.
/// </summary>
/// <remarks>
/// A wrapper over the raw JSON rather than a deserialised record, because TalentApply publishes
/// <c>PartnerApiJobResource</c> as an empty schema - <c>{"type":"array","items":{}}</c>. Every
/// other resource in their spec is fully documented; the one describing a job is not, so the
/// field names here are not from the contract.
///
/// They are taken from <c>ApplicationResource</c>, which TalentApply does document and which
/// describes the same underlying listing: <c>job_title</c>, <c>company_name</c>, <c>location</c>,
/// <c>job_slug</c>, <c>job_deadline_at</c>, <c>apply_url</c>. Plus <c>view_url</c>, named
/// explicitly in the /jobs description. That is inference from their own naming, and it is still
/// inference, so each field tries a small set of candidates and <see cref="UnreadFields"/> reports
/// what was actually there.
///
/// The first real response settles this. Until one has been seen, a listing that cannot yield a
/// title and a link is skipped rather than stored half-read - a row with an empty title is worse
/// than no row, because it reaches a person's feed looking like a bug in Radar.
/// </remarks>
public sealed class TalentApplyJob
{
    private readonly JsonElement _raw;

    public TalentApplyJob(JsonElement raw) => _raw = raw;

    public string? Title => First("title", "job_title", "name", "role_title");

    public string? Organisation => First("company_name", "company", "employer_name", "organisation");

    public string? Location => First("location", "city", "location_name");

    public string? Description => First("description", "summary", "job_description", "about");

    public string? EmploymentType => First("type", "employment_type", "job_type");

    public string? WorkArrangement => First("work_arrangement", "arrangement", "workplace_type");

    /// <summary>The link that carries partner attribution.</summary>
    /// <remarks>
    /// Deliberately not falling back to a bare job URL. The whole value of the agreement is that a
    /// candidate arriving through view_url is credited to Radar automatically; apply_url and a
    /// slug-built link reach the same posting and earn nothing. A listing with no view_url is a
    /// listing worth skipping, so this returns null rather than quietly substituting one that
    /// loses the credit - the kind of substitution that works perfectly and costs money.
    /// </remarks>
    public string? AttributedUrl => First("view_url");

    public string? Slug => First("slug", "job_slug");

    /// <summary>The closing date, where the listing carries one.</summary>
    public DateTime? Deadline
    {
        get
        {
            var raw = First("deadline_at", "job_deadline_at", "deadline", "closes_at", "expires_at");
            return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : null;
        }
    }

    /// <summary>Whether enough was read to store this as an opportunity at all.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(AttributedUrl);

    /// <summary>
    /// The property names present on the listing, for pinning the mapping from a real response.
    /// </summary>
    /// <remarks>
    /// Logged once per run rather than guessed at indefinitely. Names only, never values: this
    /// goes to the application log, and a job listing's contents are TalentApply's to publish.
    /// </remarks>
    public IReadOnlyList<string> FieldNames => _raw.ValueKind == JsonValueKind.Object
        ? [.. _raw.EnumerateObject().Select(property => property.Name)]
        : [];

    private string? First(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!_raw.TryGetProperty(candidate, out var value)) continue;

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),

                // Their resources nest named things as {id, name, ...} - work_role and country
                // both do. Reading the name is better than rendering an object id at somebody.
                JsonValueKind.Object when value.TryGetProperty("name", out var name)
                    => name.GetString(),

                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }
}
