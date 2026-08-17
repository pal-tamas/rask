namespace Rask.Bootstrap;

// One column of a BsRow: <div class="col">. Each span prop is a width in Bootstrap's 12-unit grid at ONE
// breakpoint, and they stack exactly as the class names do — Span is the unprefixed base that applies from
// the narrowest width up, Sm…Xxl take over from their own breakpoint up:
//
//   BsCol()                 → <div class="col">              equal width, sharing the row with its siblings
//   BsCol(Auto: true)       → <div class="col-auto">         just wide enough for its content
//   BsCol(Md: 6)            → <div class="col-md-6">         full width below md, half from md up
//   BsCol(Span: 7, Sm: 8)   → <div class="col-7 col-sm-8">
//   BsCol(Md: 6, Lg: 4)     → <div class="col-md-6 col-lg-4">
//
// A column with no span at any breakpoint falls back to the equal-width .col. A column WITH one
// deliberately does not also get .col: `col-md-6` alone is full width below md (Bootstrap gives
// .row > * width:100%), whereas `col col-md-6` is equal-width below md — a different layout, still
// reachable via Class: Grid.Col.
//
// Auto and Span are two ways to fill the SAME unprefixed slot, so they're alternatives, not additions:
// Auto wins if both are set. (Emitting both would be worse than picking — `col-auto col-7` puts two
// equal-specificity rules on one element, and .col-7 is the later one in the stylesheet, so the column
// would silently ignore Auto and the markup would give no hint.) Pairing Auto with a *breakpoint* span
// is a different thing and fully supported: BsCol(Auto: true, Md: 6) → `col-auto col-md-6`, content-width
// below md and half from md up.
//
// (Not to be confused with BsColumn<T>, which is a data-grid column definition — a config object passed to
// BsDataGrid's Columns, not a component.)

/// <summary>
///     A grid column, in a twelve-column layout. Set the breakpoint props for the widths where the layout
///     should change, and leave the rest to inherit.
/// </summary>
public sealed partial class BsCol : BsBlock
{
    /// <summary>How many of the twelve columns to occupy at every breakpoint.</summary>
    public int? Span { get; set; }

    /// <summary>Sizes the column to its content instead of a fixed share.</summary>
    public bool? Auto { get; set; }

    /// <summary>The span from the small breakpoint up.</summary>
    public int? Sm { get; set; }

    /// <summary>The span from the medium breakpoint up.</summary>
    public int? Md { get; set; }

    /// <summary>The span from the large breakpoint up.</summary>
    public int? Lg { get; set; }

    /// <summary>The span from the extra-large breakpoint up.</summary>
    public int? Xl { get; set; }

    /// <summary>The span from the largest breakpoint up.</summary>
    public int? Xxl { get; set; }

    protected override Component? Render()
    {
        // Null when no span was asked for at any breakpoint (Join returns null for an all-null set) —
        // which is precisely the case that wants the equal-width .col.
        var spans = BsClass.Join(
            Auto is true ? Grid.ColAuto : Span is { } span ? Grid.Column(span) : null,
            Sm is { } sm ? Grid.Column(sm, Bp.Sm) : null,
            Md is { } md ? Grid.Column(md, Bp.Md) : null,
            Lg is { } lg ? Grid.Column(lg, Bp.Lg) : null,
            Xl is { } xl ? Grid.Column(xl, Bp.Xl) : null,
            Xxl is { } xxl ? Grid.Column(xxl, Bp.Xxl) : null);

        return Wrap(spans ?? Grid.Col);
    }
}
