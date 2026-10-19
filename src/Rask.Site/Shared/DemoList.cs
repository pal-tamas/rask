namespace Rask.Site;

/// <summary>
///     How a demo draws a list of rows: on the surface of a Flux card, one hairline between rows.
/// </summary>
/// <remarks>
///     Flux has no list component, so a demo writes its own <c>Ul</c> or <c>Ol</c> with these classes and keeps every
///     attribute an element takes. The rows are padded from here (<c>*:</c>), so a row only says what it holds.
/// </remarks>
internal static class DemoList
{
    /// <summary>A <c>&lt;ul&gt;</c>'s classes.</summary>
    internal const string Rows =
        "overflow-hidden rounded-xl border border-zinc-900/10 bg-white text-sm shadow-xs divide-y divide-zinc-900/10 "
        + "dark:border-white/10 dark:bg-white/10 dark:shadow-none dark:divide-white/10 *:px-4 *:py-2.5";

    /// <summary>A numbered <c>&lt;ol&gt;</c>'s, for a sequence whose order means something, such as a log.</summary>
    internal const string Numbered = Rows + " list-decimal list-inside";
}
