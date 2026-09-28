using System.Globalization;
using Rask.Core.Live;

namespace Rask.DevTools.Probe;

/// <summary>Places on the page, in the client's own coordinates, shared by the tree and the render flash.</summary>
internal static class DevToolsPlaces
{
    /// <summary>
    ///     Where the nodes written between <paramref name="start" /> and <paramref name="end" /> sit on the page, as
    ///     <c>path|firstSlot|count</c>, or null when they are no nodes the client can address. <paramref name="path" /> is
    ///     scratch, reused by the caller.
    /// </summary>
    internal static string? Locate(ReadOnlySpan<RenderFrame> frames, int start, int end, List<int> path)
    {
        if (start < 0 || end > frames.Length || start > end
            || !FramePathWalker.TryResolve(frames, start, end, path, out var first, out var count)
            || count == 0)
        {
            return null;
        }

        return string.Join('.', path) + "|" + first.ToString(CultureInfo.InvariantCulture) + "|"
               + count.ToString(CultureInfo.InvariantCulture);
    }
}
