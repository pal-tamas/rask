namespace Rask;

/// <summary>What a heading cell and a data cell of a <see cref="UiTable" /> share: their box.</summary>
internal static class UiTableBox
{
    /// <summary>
    ///     12px all round, flush with the table's edge in the first and last column — or, in a bleeding
    ///     table, in from it by the distance the table bled out.
    /// </summary>
    /// <remarks>
    ///     The 12px is written at zero specificity, so a cell's own <c>py-0</c> or <c>px-6</c> wins whatever
    ///     order the stylesheet happens to emit the two in. Flux merges the classes on the server to the same
    ///     end.
    /// </remarks>
    internal const string Padding =
        "[:where(&)]:px-3 [:where(&)]:py-3 first:ps-0 last:pe-0 [[data-ui-table-bleed]_&]:first:ps-[var(--ui-bleed,1.5rem)] [[data-ui-table-bleed]_&]:last:pe-[var(--ui-bleed,1.5rem)]";

    /// <summary>
    ///     Held at the scroll area's left edge. The <c>::after</c> hangs off its right side and carries the
    ///     shadow the stylesheet draws once the columns beside it have scrolled underneath.
    /// </summary>
    internal const string Stuck =
        "sticky left-0 z-10 after:pointer-events-none after:absolute after:inset-y-0 after:right-0 after:w-8 after:translate-x-full after:content-['']";
}
