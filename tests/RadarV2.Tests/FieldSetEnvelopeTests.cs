using RadarV2.Helpers;
using RadarV2.Models;

namespace RadarV2.Tests;

/// <summary>
/// The field projector rebuilds the page envelope by hand, so every field has to be copied
/// explicitly — and forgetting one is silent.
///
/// `EmptyReason` was added to <see cref="PagedResult{T}"/> and not added here. The API then
/// reported `NotEmpty` on every response, including responses with no items, so the client's
/// carefully-worded empty states never appeared. Nothing failed: the field was present, correctly
/// typed, and always wrong. A green test suite and a clean build said everything was fine, and it
/// took reading an actual HTTP response to see it.
///
/// This test fails if a field is ever added to the envelope and dropped here again.
/// </summary>
public class FieldSetEnvelopeTests
{
    private static PagedResult<string> Page() => new()
    {
        Items = ["one", "two"],
        NextCursor = "cursor-value",
        HasMore = true,
        EmptyReason = FeedEmptyReason.EverythingDismissed,
    };

    private static readonly Dictionary<string, Func<string, object?>> Projectors =
        new(StringComparer.OrdinalIgnoreCase) { ["value"] = s => s };

    [Fact]
    public void Projecting_with_no_field_selection_keeps_the_whole_envelope()
    {
        FieldSet.TryParse(null, Projectors.Keys.ToList(), out var fieldSet, out _);

        var projected = fieldSet.ProjectPage(Page(), Projectors);

        Assert.Equal("cursor-value", projected.NextCursor);
        Assert.True(projected.HasMore);
        Assert.Equal(FeedEmptyReason.EverythingDismissed, projected.EmptyReason);
    }

    [Fact]
    public void Projecting_a_subset_of_fields_still_keeps_the_whole_envelope()
    {
        // The envelope describes the page; narrowing which fields of an *item* are returned must
        // not change what the page says about itself.
        FieldSet.TryParse("value", Projectors.Keys.ToList(), out var fieldSet, out _);

        var projected = fieldSet.ProjectPage(Page(), Projectors);

        Assert.Equal("cursor-value", projected.NextCursor);
        Assert.True(projected.HasMore);
        Assert.Equal(FeedEmptyReason.EverythingDismissed, projected.EmptyReason);
    }

    [Fact]
    public void An_empty_page_carries_its_reason_through_projection()
    {
        FieldSet.TryParse(null, Projectors.Keys.ToList(), out var fieldSet, out _);

        var empty = new PagedResult<string>
        {
            Items = [],
            HasMore = false,
            EmptyReason = FeedEmptyReason.NoneOfThisType,
        };

        var projected = fieldSet.ProjectPage(empty, Projectors);

        Assert.Empty(projected.Items);
        Assert.Equal(FeedEmptyReason.NoneOfThisType, projected.EmptyReason);
    }
}
