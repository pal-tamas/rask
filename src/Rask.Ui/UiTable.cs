namespace Rask.Ui;

/// <summary>
/// A table that scrolls on its own rather than making the page scroll sideways.
/// </summary>
/// <remarks>
/// The header row and the cell padding live here so every table on the console matches; a page supplies
/// only its <c>thead</c> and <c>tbody</c>.
/// <para>
/// The horizontal scroll is a backstop, not the mobile plan. A table an operator has to swipe sideways to
/// read has hidden the column they came for, so pages drop their secondary columns below <c>sm</c>
/// (<c>hidden sm:table-cell</c>) and let the first cell carry the stacked detail instead. One markup, two
/// shapes — rather than a table and a card list that have to be kept saying the same thing.
/// </para>
/// </remarks>
public sealed partial class UiTable : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("overflow-x-auto rounded-xl border border-base-300 bg-base-100")[
            Table.Class("w-full border-collapse text-left text-sm")[Children ?? []]
        ];
}
