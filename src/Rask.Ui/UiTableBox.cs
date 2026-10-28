namespace Rask;

/// <summary>What a heading cell and a data cell of a <see cref="UiTable" /> share: their sticky edge and their class join.</summary>
internal static class UiTableBox
{
    /// <summary>
    ///     Flux's sticky cell: held at the scroll area's left edge in the first column, its right edge in the last. The
    ///     <c>::after</c> hangs off the inner side and carries the shadow <c>ui.css</c> draws once columns have scrolled
    ///     under it (Flux's <c>in-data-scrolled-*</c> waits on a mark its script writes, which the kit has no script for).
    /// </summary>
    internal const string Stuck =
        "z-10 first:sticky first:left-0 last:sticky last:right-0 "
        + "first:after:w-8 first:after:absolute first:after:inset-y-0 first:after:right-0 first:after:translate-x-full first:after:pointer-events-none "
        + "last:after:w-8 last:after:absolute last:after:inset-y-0 last:after:left-0 last:after:-translate-x-full last:after:pointer-events-none "
        + "in-data-scrolled-right:first:after:inset-shadow-[8px_0px_8px_-8px_rgba(0,0,0,0.05)] "
        + "in-data-scrolled-left:last:after:inset-shadow-[-8px_0px_8px_-8px_rgba(0,0,0,0.05)]";

    /// <summary>Flux's parts, in Flux's order, joined once: the empty ones left out.</summary>
    internal static string Join(params string[] parts) => string.Join(' ', parts.Where(part => part.Length > 0));

    /// <summary>
    ///     The part's own list with the call site's class after it. A table renders thousands of cells, so the list is
    ///     handed back as it is when there is no class, and joined in one exact-size step when there is.
    /// </summary>
    internal static string WithClass(string list, string? @class) =>
        string.IsNullOrWhiteSpace(@class) ? list : string.Concat(list, " ", @class.Trim());
}
