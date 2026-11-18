namespace Rask;

/// <summary>
///     A filter a <see cref="UiFilters" /> cannot read from a control by itself: the page says what it is called, what
///     it holds and how it is taken off.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Filter.Label("Tags").Shown(Tags).OnRemove(ClearTags)[
///         Ui.Select.Values(Tags).OnChange(SetTags).Listbox.Multiple()[…]
///     ]
///     </code>
///     <para>
///     In force while <see cref="Shown" /> is set; an empty one shows the label alone. Handed the names it holds —
///     <c>.Shown(Tags)</c>, <see cref="UiFilterShown.Shown(UiFilter, IEnumerable{string})" /> — it says one by its name
///     and two or more by their count ("Tags: 3 selected"), all of them in a tooltip on the badge; none is not in force. What it holds is drawn in the
///     set's panel, and with nothing in it the filter is a badge only. Not a Flux component: the owner's (2026-10-21).
///     </para>
/// </remarks>
public sealed partial class UiFilter : Component, IUiFilterField, ICarriesHandSteps
{
    private static readonly UiPartMarker Marker = new("ui-filter");

    // The names the last Shown(names) was handed, and what it wrote into Shown for them: a Shown written since, by
    // the text step, is the page's own words, and the names no longer say it.
    private IReadOnlyList<string>? _names;
    private string? _namesSaid;

    // A range's two ends (Ui.Filter.Range), and the set it was last handed to, whose address its fields write.
    private IUiFilterEnds? _ends;
    private UiFilters? _set;

    /// <summary>What it filters by: the badge's first word.</summary>
    public required string Label { get; set; }

    /// <summary>What it holds, as the badge says it. Unset, the filter is not in force and has no badge.</summary>
    public string? Shown { get; set; }

    /// <summary>Runs when its badge is taken off, or every filter is cleared: where the page takes the filter off.</summary>
    public Callback OnRemove { get; set; }

    /// <inheritdoc />
    UiFilterEntry IUiFilterField.Filter() => _ends?.Filter(Label) ?? new(Label, Shown, Shown is not null, null)
    {
        OnRemove = OnRemove,
        Identity = Key,
        Names = _names is { Count: > 1 } names && ReferenceEquals(Shown, _namesSaid) ? names : null,
    };

    /// <summary>What <see cref="UiFilterShown.Shown(UiFilter, IEnumerable{string})" /> sets: the names, and how the badge says them.</summary>
    internal UiFilter Holding(IEnumerable<string> names)
    {
        List<string> held = [.. names];
        _names = held;
        _namesSaid = held.Count switch
        {
            0 => null,
            1 => held[0],
            _ => RaskStrings.Get(RaskString.FiltersSelectedCount, "{0} selected", held.Count),
        };
        Shown = _namesSaid;
        return this;
    }

    /// <summary>What <see cref="UiFilterRange.Range{T}" /> sets: the two ends the filter draws and says.</summary>
    internal UiFilter Ranging(IUiFilterEnds ends)
    {
        _ends = ends;
        return this;
    }

    // What it holds is the page's to bind, and a range's fields are drawn bound to the set's address.
    /// <inheritdoc />
    void IUiFilterField.Take(UiFilters set) => _set = set;

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiFilter other)
        {
            other._ends = _ends;
            other._names = _names;
            other._namesSaid = _namesSaid;
        }
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (_ends is { } ends)
        {
            return Div.Data(Marker.With(null))[ends.Draw(Label, _set), Children ?? []];
        }

        return Children is null ? null : Div.Data(Marker.With(null))[Children];
    }
}
