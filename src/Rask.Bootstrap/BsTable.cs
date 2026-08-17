namespace Rask.Bootstrap;

// A Bootstrap table: <table class="table …">. Wraps the core Table() with the typed style toggles;
// set Responsive to wrap it in a .table-responsive scroll container. Children are the usual thead/
// tbody/tr/td markup (core Thead/Tbody/Tr/Td or plain elements).

/// <summary>
///     A styled data table. For sorting, paging, grouping and selection, use <c>BsDataGrid</c> instead of
///     building them on top of this.
/// </summary>
public sealed partial class BsTable : BsBlock
{
    /// <summary>The table's semantic colour variant.</summary>
    public BsColor? Color { get; set; }

    /// <summary>Shades alternate rows.</summary>
    public bool? Striped { get; set; }

    /// <summary>Shades alternate columns.</summary>
    public bool? StripedColumns { get; set; }

    /// <summary>Draws borders on every cell.</summary>
    public bool? Bordered { get; set; }

    /// <summary>Removes all borders.</summary>
    public bool? Borderless { get; set; }

    /// <summary>Highlights the row under the pointer.</summary>
    public bool? Hover { get; set; }

    /// <summary>Tightens the cell padding.</summary>
    public new bool? Small { get; set; }

    /// <summary>Scrolls the table horizontally on narrow screens instead of overflowing the page.</summary>
    public bool? Responsive { get; set; }

    // ARIA passthrough onto the <table> itself (not the responsive wrapper), the same shape BsButton
    // exposes: each entry emits aria-{key}. This is how a caller marks the table aria-busy while it
    // refetches without the wrapper swallowing the state.
    /// <summary>
    ///     ARIA states and properties on the rendered element. Each entry emits <c>aria-{key}="{value}"</c>,
    ///     so <c>.Aria("label", "Close")</c> renders <c>aria-label="Close"</c> — give the key without the
    ///     prefix.
    ///     <para>
    ///         State belongs here as much as labels: <c>aria-expanded</c> and <c>aria-current</c> have to
    ///         change as the component does, or assistive technology is told the opposite of what is shown.
    ///     </para>
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Aria { get; set; }

    // Bounds the table's height (any CSS length: "400px", "60vh"), making it scroll vertically inside
    // its own container instead of running down the page. This is also what StickyHeader needs: a
    // sticky header sticks to the nearest scroll container, so with nothing bounding the height there
    // is nothing to stick to.

    /// <summary>Caps the table's height and scrolls inside it.</summary>
    public string? MaxHeight { get; set; }

    // Freezes the header row while the body scrolls under it. Only does something inside a bounded
    // scroll container — pair it with MaxHeight (see the .bs-table-sticky rule in rask-bootstrap.css).

    /// <summary>Keeps the header visible while the body scrolls.</summary>
    public bool? StickyHeader { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "table",
            Color is { } c ? c.Table() : null,
            Striped is true ? "table-striped" : null,
            StripedColumns is true ? "table-striped-columns" : null,
            Bordered is true ? "table-bordered" : null,
            Borderless is true ? "table-borderless" : null,
            Hover is true ? "table-hover" : null,
            Small is true ? "table-sm" : null,
            StickyHeader is true ? "bs-table-sticky" : null,
            Class);

        var table = Table.Id(Id).Class(cls).Aria(Aria)[Items];

        // MaxHeight implies the wrapper even when Responsive is off: the height has to bound a scroll
        // container, and without one it would just clip. .table-responsive declares only overflow-x, but
        // a non-visible overflow on one axis computes the other to auto — so max-height on that same
        // element is what actually makes the body scroll vertically.
        return Responsive is true || MaxHeight is not null
            ? Div.Class("table-responsive").Style(MaxHeight is not null ? $"max-height:{MaxHeight}" : null)[table]
            : table;
    }
}
