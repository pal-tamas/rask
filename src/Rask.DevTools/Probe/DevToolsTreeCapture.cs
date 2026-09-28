using Rask.Core;
using Rask.Core.Live;

namespace Rask.DevTools.Probe;

/// <summary>
///     What the page's last render walk left behind — enough to build its tree later, without walking it again.
/// </summary>
/// <remarks>
///     <para>
///         Kept for every render of an inspected session, so a panel that opens between renders has a tree at once. That
///         is why it is buffers rather than nodes: it is overwritten in place, and allocates nothing once it has grown to
///         the page's size. Nodes are built only when a panel asks for them.
///     </para>
///     <para>
///         Written under the session's render gate and read under it, never between: the frames are copied out of the
///         writer because the writer is reset by the next build, and the components are only read while no walk can be
///         changing them.
///     </para>
/// </remarks>
internal sealed class DevToolsTreeCapture
{
    private DevToolsWalkItem[] _items = [];
    private RenderFrame[] _frames = [];
    private DevToolsProvideItem[] _provides = [];
    private DevToolsReadItem[] _reads = [];
    private int _provideCount;
    private int _readCount;

    internal Component? Root { get; private set; }

    internal int ItemCount { get; private set; }

    /// <summary>How many frames were copied, or -1 when the walk captured none (the tree then has no tags).</summary>
    internal int FrameCount { get; private set; } = -1;

    internal ReadOnlySpan<DevToolsWalkItem> Items => _items.AsSpan(0, ItemCount);

    internal ReadOnlySpan<RenderFrame> Frames => FrameCount < 0 ? default : _frames.AsSpan(0, FrameCount);

    /// <summary>Whether the walk recorded context: false for a render made while no panel showed the tree.</summary>
    internal bool HasContexts { get; private set; }

    internal ReadOnlySpan<DevToolsProvideItem> Provides => _provides.AsSpan(0, _provideCount);

    internal ReadOnlySpan<DevToolsReadItem> Reads => _reads.AsSpan(0, _readCount);

    internal void Record(
        Component root, List<DevToolsWalkItem> items, FrameWriter? frames,
        List<DevToolsProvideItem>? provides = null, List<DevToolsReadItem>? reads = null)
    {
        Root = root;
        ItemCount = CopyInto(ref _items, items, ItemCount);
        HasContexts = provides is not null && reads is not null;
        _provideCount = CopyInto(ref _provides, provides, _provideCount);
        _readCount = CopyInto(ref _reads, reads, _readCount);

        if (frames is null)
        {
            FrameCount = -1;
            return;
        }

        var written = frames.WrittenSpan;
        if (_frames.Length < written.Length)
        {
            _frames = new RenderFrame[Math.Max(written.Length, _frames.Length * 2)];
        }

        written.CopyTo(_frames);
        FrameCount = written.Length;
    }

    // Grows the buffer to fit, copies, and clears the old tail — which still names components and values from an earlier
    // render, and would keep them alive. Returns the new count.
    private static int CopyInto<T>(ref T[] buffer, List<T>? from, int previousCount)
    {
        var count = from?.Count ?? 0;
        if (buffer.Length < count)
        {
            buffer = new T[Math.Max(count, buffer.Length * 2)];
        }

        from?.CopyTo(buffer);
        Array.Clear(buffer, count, Math.Max(0, previousCount - count));
        return count;
    }
}
