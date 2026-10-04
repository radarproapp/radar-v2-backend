namespace RadarV2.Helpers;

/// <summary>
/// Sparse fieldset selection for list responses (<c>?fields=id,title,source</c>).
///
/// The name list is matched against a whitelist supplied by the caller rather than reflected over
/// the model, for two reasons: it keeps the set of fields a client can ask for an explicit decision
/// (nobody can enumerate internals we did not mean to expose), and it avoids paying reflection cost
/// on the hot list path that this feature exists to make cheaper.
///
/// An unknown name is rejected rather than ignored — a silent omission would look like a backend
/// bug to the caller instead of surfacing their typo.
/// </summary>
public sealed class FieldSet
{
    private readonly IReadOnlyList<string> _names;

    private FieldSet(IReadOnlyList<string> names) => _names = names;

    /// <summary>True when the caller asked for no projection, i.e. they want the full object.</summary>
    public bool IsEmpty => _names.Count == 0;

    public IReadOnlyList<string> Names => _names;

    public static bool TryParse(
        string? raw,
        IReadOnlyCollection<string> allowed,
        out FieldSet fields,
        out string? error)
    {
        // Rejected explicitly rather than treated as "no projection": dropping a malformed
        // selector silently would send more data than the client budgeted for.
        if (string.IsNullOrWhiteSpace(raw))
        {
            fields = new FieldSet([]);
            error = null;
            return true;
        }

        var requested = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unknown = requested.Where(name => !allowed.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            fields = new FieldSet([]);
            error = $"Unknown field(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", allowed)}.";
            return false;
        }

        fields = new FieldSet(requested);
        error = null;
        return true;
    }

    /// <summary>
    /// Projects <paramref name="item"/> down to the requested fields. Returns the item untouched when
    /// nothing was requested, so the un-filtered path stays allocation-free.
    /// </summary>
    public object Project<T>(T item, IReadOnlyDictionary<string, Func<T, object?>> projectors)
    {
        if (IsEmpty) return item!;

        var projected = new Dictionary<string, object?>(_names.Count, StringComparer.Ordinal);
        foreach (var name in _names)
        {
            var projector = projectors.FirstOrDefault(pair =>
                string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
            projected[projector.Key ?? name] = projector.Value is null ? null : projector.Value(item);
        }

        return projected;
    }

    /// <summary>Projects a whole page, converting the envelope so <c>nextCursor</c> survives.</summary>
    public Models.PagedResult<object> ProjectPage<T>(
        Models.PagedResult<T> page,
        IReadOnlyDictionary<string, Func<T, object?>> projectors)
    {
        if (IsEmpty)
            return new Models.PagedResult<object>
            {
                Items = [.. page.Items.Select(item => (object)item!)],
                NextCursor = page.NextCursor,
                HasMore = page.HasMore,
            };

        return new Models.PagedResult<object>
        {
            Items = [.. page.Items.Select(item => Project(item, projectors))],
            NextCursor = page.NextCursor,
            HasMore = page.HasMore,
        };
    }
}
