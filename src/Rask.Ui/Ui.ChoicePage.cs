using System.Runtime.InteropServices;

namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     Which rows a paging <c>.Choices(find, held:)</c> asks for: those after the first <paramref name="Skip" />, and at
    ///     most <paramref name="Take" /> of them — <c>query.Skip(page.Skip).Take(page.Take)</c>.
    /// </summary>
    /// <remarks>
    ///     Asked for the first page whenever what is typed changes, and for the next one as the reader scrolls near the end
    ///     of the rows already in the list. An answer of fewer rows than <paramref name="Take" /> is the end of the list:
    ///     nothing past it is asked for until something else is typed.
    /// </remarks>
    /// <param name="Skip">How many rows the list holds already: the ones to skip.</param>
    /// <param name="Take">The most rows to answer with.</param>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct ChoicePage(int Skip, int Take);
}
