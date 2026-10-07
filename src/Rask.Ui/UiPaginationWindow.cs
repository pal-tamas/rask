namespace Rask;

/// <summary>
///     Which page numbers a pager draws, and where it leaves a run of them out.
/// </summary>
/// <remarks>
///     Laravel's window, which Flux's pager shows: three pages either side of the current one, the first two
///     and the last two always, and every page when there are fewer than fourteen. Near either end the
///     window slides against it — ten pages from that end — so the pager does not shrink as the reader
///     approaches it. Zero stands for a gap.
/// </remarks>
internal static class UiPaginationWindow
{
    internal const int Gap = 0;

    private const int EachSide = 3;

    // The current page, three either side of it, and the first and last pairs with a gap beside each.
    private const int Edge = EachSide + 4;

    internal static IEnumerable<int> Of(int current, int last)
    {
        if (last < (EachSide * 2) + 8)
        {
            return Enumerable.Range(1, last);
        }

        if (current <= Edge)
        {
            return [.. Enumerable.Range(1, Edge + EachSide), Gap, last - 1, last];
        }

        if (current > last - Edge)
        {
            return [1, 2, Gap, .. Enumerable.Range(last - (Edge + EachSide - 1), Edge + EachSide)];
        }

        return [1, 2, Gap, .. Enumerable.Range(current - EachSide, (EachSide * 2) + 1), Gap, last - 1, last];
    }
}
