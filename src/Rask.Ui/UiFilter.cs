namespace Rask;

/// <summary>
///     A filter a <see cref="UiFilters" /> cannot read from a control by itself: the page says what it is called, what
///     it holds and how it is taken off.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Filter.Label("Tags").Shown(Tags.Count == 0 ? null : string.Join(", ", Tags)).OnRemove(ClearTags)[
///         Ui.Select.Values(Tags).OnChange(SetTags).Listbox.Multiple()[…]
///     ]
///     </code>
///     <para>
///     In force while <see cref="Shown" /> is set; an empty one shows the label alone. What it holds is drawn in the
///     set's panel, and with nothing in it the filter is a badge only. Not a Flux component: the owner's (2026-10-21).
///     </para>
/// </remarks>
public sealed partial class UiFilter : Component, IUiFilterField
{
    private static readonly UiPartMarker Marker = new("ui-filter");

    /// <summary>What it filters by: the badge's first word.</summary>
    public required string Label { get; set; }

    /// <summary>What it holds, as the badge says it. Unset, the filter is not in force and has no badge.</summary>
    public string? Shown { get; set; }

    /// <summary>Runs when its badge is taken off, or every filter is cleared: where the page takes the filter off.</summary>
    public Callback OnRemove { get; set; }

    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter() => new(Label, Shown, Shown is not null, null) { OnRemove = OnRemove, Identity = Key };

    // What it holds is the page's to bind: nothing to hand over.
    /// <inheritdoc />
    void IUiFilterField.Take(UiFilters set)
    {
    }

    /// <inheritdoc />
    protected override Component? Render() => Children is null ? null : Div.Data(Marker.With(null))[Children];
}
