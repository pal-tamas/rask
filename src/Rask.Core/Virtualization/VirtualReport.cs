using System.Runtime.InteropServices;
using System.Text.Json;

namespace Rask.Core.Virtualization;

/// <summary>
///     What the browser measured of a virtual list's drawn rows when its box scrolled (rask-input.ts): where the list
///     starts in the box, how tall the rows between its two spacers are, which rows those were, and which of them the
///     box's two edges cut.
/// </summary>
/// <param name="ListTop">How far below the top of the box's scrolled content the list's first spacer starts, in pixels.</param>
/// <param name="Separation">The room between the two spacers — the drawn rows' heights together — in pixels.</param>
/// <param name="Rows">How many elements stand between the spacers.</param>
/// <param name="Start">The first row drawn, as the spacer above them says.</param>
/// <param name="Done">The last scroll-to the browser carried out (<c>data-rask-virtual-to</c>); -1 while the hook has not run.</param>
/// <param name="TopRow">The drawn row the box's top edge cuts (or the first below it); -1 when it cuts none.</param>
/// <param name="BottomRow">The drawn row the box's bottom edge cuts (or the last above it); -1 when it cuts none.</param>
/// <remarks>
///     The rows the edges cut are the browser's own answer and the one to go by inside the drawn window: rows of mixed
///     heights stand where they stand, and an answer worked out from the window's average height would differ from one
///     window to the next — two windows each finding the other, for ever.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct VirtualReport(
    double ListTop, double Separation, int Rows, int Start, int Done, int TopRow, int BottomRow)
{
    private const int Fields = 7;

    // The client's word, bounded as VirtualScroll bounds its scrollTop: nothing in it may overflow a sum.
    private const double MaxPixels = int.MaxValue / 4.0;

    private const int MaxCount = int.MaxValue / 2;

    /// <summary>
    ///     Reads <c>raskVirtual: [listTop, separation, rows, start, done, topRow, bottomRow]</c> off a scroll target; null
    ///     when absent or malformed.
    /// </summary>
    /// <param name="target">The event's target state.</param>
    /// <returns>The report, or null.</returns>
    internal static VirtualReport? Read(JsonElement target)
    {
        if (target.ValueKind != JsonValueKind.Object
            || !target.TryGetProperty("raskVirtual", out var v)
            || v.ValueKind != JsonValueKind.Array
            || v.GetArrayLength() != Fields)
        {
            return null;
        }

        Span<double> n = stackalloc double[Fields];
        var i = 0;
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out var d) || !double.IsFinite(d))
            {
                return null;
            }

            n[i++] = d;
        }

        return new VirtualReport(
            Math.Clamp(n[0], 0, MaxPixels),
            Math.Clamp(n[1], 0, MaxPixels),
            Count(n[2], 0),
            Count(n[3], 0),
            Count(n[4], -1),
            Count(n[5], -1),
            Count(n[6], -1));
    }

    /// <summary>Whether this measured <paramref name="drawn" />: the rows between the spacers are the ones that render drew.</summary>
    /// <param name="drawn">The window the last render drew.</param>
    /// <returns>True when the report is of that window.</returns>
    internal bool Of(VirtualWindow drawn) => Start == drawn.Start && Rows == drawn.Count;

    private static int Count(double n, int least) => (int)Math.Clamp(n, least, MaxCount);
}
