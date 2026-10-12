namespace Rask;

/// <summary>What a heading cell and a data cell of a <see cref="UiTable" /> share: their box.</summary>
internal static class UiTableBox
{
    /// <summary>
    ///     12px all round, flush with the table's edge in the first and last column — or, in a bleeding
    ///     table, in from it by the distance the table bled out.
    /// </summary>
    /// <remarks>
    ///     All of it at zero specificity, and no two rules for one side of one cell: a cell's own <c>py-0</c>,
    ///     <c>px-6</c> or <c>ps-10</c> wins in every column, and nothing depends on the order the stylesheet
    ///     emits the kit's own rules in. Flux merges the classes on the server to the same end.
    /// </remarks>
    internal const string Padding =
        "[:where(&)]:py-3 [:where(&:not(:first-child))]:ps-3 [:where(&:not(:last-child))]:pe-3 "
        + "[:where([data-ui-table-bleed]_&:first-child)]:ps-[var(--ui-bleed,1.5rem)] "
        + "[:where([data-ui-table-bleed]_&:last-child)]:pe-[var(--ui-bleed,1.5rem)]";

    /// <summary>
    ///     Held at the scroll area's left edge. The <c>::after</c> hangs off its right side and carries the
    ///     shadow the stylesheet draws once the columns beside it have scrolled underneath.
    /// </summary>
    internal const string Stuck =
        "sticky left-0 z-10 after:pointer-events-none after:absolute after:inset-y-0 after:right-0 after:w-8 after:translate-x-full after:content-['']";
}
