namespace Rask.Bootstrap;

// A Bootstrap grid row: <div class="row">. Holds BsCol children and supplies their gutters.
//
//   BsRow(Gutter: 3)[BsCol(Md: 6)[…], BsCol(Md: 6)[…]]   → <div class="row g-3">
//
// A row sets a negative side margin of half a gutter, which its columns' matching padding cancels — so it
// belongs inside a BsContainer (or anything else padding its sides) rather than bare on the page, where
// that negative margin has nothing to cancel against and the row overhangs the viewport.
//
// Gutter is the space between columns on both axes (.g-0 … .g-5). Note it is NOT flex gap: Bootstrap
// implements it as column padding plus that row margin. The row declares --bs-gutter-x on itself, so this
// is the knob — setting the variable on a surrounding container has no effect on the columns.
//
// Nothing else earns a prop here. Vertically centring columns of unequal height — the only other thing the
// samples ask a row for — is BsRow(Class: Flex.Align(BsAlign.Center)): already typed, already composable,
// and not worth a second way to say it.

/// <summary>
///     A grid row, and the required parent of every <c>BsCol</c>. It supplies the negative margins that
///     cancel the columns' gutters.
/// </summary>
public sealed partial class BsRow : BsBlock
{
    /// <summary>The spacing between columns in this row.</summary>
    public int? Gutter { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join(Grid.Row, Gutter is { } gutter ? Grid.Gutter(gutter) : null, Class))[Items];
}
