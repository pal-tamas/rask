using System.Globalization;
using System.Runtime.CompilerServices;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;

namespace Rask.DevTools.Probe;

/// <summary>
///     One node in the tree the panel shows: a component, or an HTML element one of them rendered.
/// </summary>
/// <remarks>
///     A SNAPSHOT, not a live reference. The panel renders on its own session, after the walk that produced this has
///     finished, and a tree of live components read from there would be read while the app mutates it — and would keep
///     every component it named alive besides.
/// </remarks>
/// <param name="Id">Stable for as long as the component lives, so a panel's expansion survives a re-render.</param>
/// <param name="Type">The component's type name, as a developer writes it, or the element's tag.</param>
/// <param name="Key">Its reconciliation key, when it has one.</param>
/// <param name="Props">Its properties, as the build described them — empty in a build without the devtools.</param>
/// <param name="Children">What it rendered, in page order.</param>
/// <param name="IsTag">An HTML element rather than a component; the panel hides these unless asked.</param>
/// <param name="At">
///     Where it is on the page, as the client addresses DOM nodes: <c>path|firstSlot|count</c> — the child slots from the
///     document to its parent, dot-separated, the slot its first node occupies, and how many sibling nodes it rendered.
///     Null when it rendered nothing, sits inside another renderer's subtree, or the walk captured no frames.
/// </param>
internal sealed record DevToolsComponentNode(
    long Id,
    string Type,
    string? Key,
    IReadOnlyList<DescribedProp> Props,
    IReadOnlyList<DevToolsComponentNode> Children,
    bool IsTag = false,
    string? At = null);

/// <summary>One component as the walk met it: what it is, what it was walked inside, and what it wrote.</summary>
internal readonly record struct DevToolsWalkItem(Component Component, Component? Parent, int FrameStart, int FrameEnd);

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

    internal Component? Root { get; private set; }

    internal int ItemCount { get; private set; }

    /// <summary>How many frames were copied, or -1 when the walk captured none (the tree then has no tags).</summary>
    internal int FrameCount { get; private set; } = -1;

    internal ReadOnlySpan<DevToolsWalkItem> Items => _items.AsSpan(0, ItemCount);

    internal ReadOnlySpan<RenderFrame> Frames => FrameCount < 0 ? default : _frames.AsSpan(0, FrameCount);

    internal void Record(Component root, List<DevToolsWalkItem> items, FrameWriter? frames)
    {
        Root = root;

        if (_items.Length < items.Count)
        {
            _items = new DevToolsWalkItem[Math.Max(items.Count, _items.Length * 2)];
        }

        items.CopyTo(_items);
        // The old tail still names components from an earlier render; cleared so it does not keep them alive.
        Array.Clear(_items, items.Count, Math.Max(0, ItemCount - items.Count));
        ItemCount = items.Count;

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
}

/// <summary>
///     Turns a capture into a tree, and hands every component an id that outlives one render.
/// </summary>
/// <remarks>
///     <para>
///         The tree is the PAGE's nesting: each component under the component it was walked inside. That is not always the
///         component that constructed it — a card's children are built by whoever wrote the card — and it is the shape a
///         developer is looking at on the page.
///     </para>
///     <para>
///         The HTML elements come from the frame stream, the same one the diff reads: an element frame inside a component's
///         span, and outside any child component's span, is that component's own element. They are always in the tree;
///         the panel leaves them out unless a developer asks for them.
///     </para>
/// </remarks>
internal sealed class DevToolsTreeSnapshotter
{
    /// <summary>How many nodes one snapshot may carry. A tree past this is truncated rather than left to grow.</summary>
    internal const int MaxNodes = 20_000;

    // A tag's id is its component's id with the tag's place among that component's own elements in the low bits, so an
    // opened <ul> stays open across renders for as long as its component renders the same elements.
    private const int TagBits = 20;

    // Weak, and by reference: the id belongs to the component, and a component that leaves the tree takes its id with it.
    private readonly ConditionalWeakTable<Component, StrongBox<long>> _ids = new();
    private long _next;

    internal DevToolsComponentNode? Snapshot(DevToolsTreeCapture capture)
    {
        if (capture.Root is not { } root)
        {
            return null;
        }

        return new Builder(this, capture).Build(root);
    }

    /// <summary>The component's id: handed out on first sight, kept for as long as the component lives.</summary>
    internal long IdOf(Component component) =>
        _ids.GetValue(component, _ => new StrongBox<long>(Interlocked.Increment(ref _next))).Value;

    private static DevToolsComponentNode Describe(
        long id, Component component, List<DevToolsComponentNode> children, string? at)
    {
        // What the component says about itself. The override the build wrote reads its own properties by name; in a
        // build without the devtools the base method is empty, so this is a call that collects nothing.
        var describer = new PropsDescriber();
        component.DescribeProps(describer);
        return new DevToolsComponentNode(
            id, DevToolsNames.Of(component.GetType()), component.Key?.ToString(), describer.Props, children, At: at);
    }

    private sealed class Builder
    {
        private readonly DevToolsTreeSnapshotter _owner;
        private readonly DevToolsTreeCapture _capture;
        private readonly Dictionary<Component, int> _index = new(ReferenceEqualityComparer.Instance);
        private readonly List<int>?[] _kids;
        private readonly List<int> _rootKids = [];
        private readonly List<int> _path = [];
        private int _budget = MaxNodes;

        internal Builder(DevToolsTreeSnapshotter owner, DevToolsTreeCapture capture)
        {
            _owner = owner;
            _capture = capture;
            var items = capture.Items;
            _kids = new List<int>?[items.Length];

            // The root is the tree's top, built by Build — never one of its own children, even when the walk reports it too
            // (a root boundary is walked like any component). Counting it twice put the whole document in the tree twice,
            // and a pick matched the copy the tree never opened.
            var root = capture.Root;
            for (var i = 0; i < items.Length; i++)
            {
                if (!ReferenceEquals(items[i].Component, root))
                {
                    _index.TryAdd(items[i].Component, i);
                }
            }

            // A parent the walk never reported, or the root itself, makes a child of the root.
            for (var i = 0; i < items.Length; i++)
            {
                if (ReferenceEquals(items[i].Component, root))
                {
                    continue;
                }

                var parent = items[i].Parent;
                if (parent is not null
                    && !ReferenceEquals(parent, items[i].Component)
                    && !ReferenceEquals(parent, root)
                    && _index.TryGetValue(parent, out var p))
                {
                    (_kids[p] ??= []).Add(i);
                }
                else
                {
                    _rootKids.Add(i);
                }
            }

            // In page order. The walk reports a component when it FINISHES, which keeps true siblings in order, but not the
            // children gathered under the root from different subtrees — and the frame walk below claims each child at the
            // frame it starts on, in list order, so one out of place lets the walk run through another's elements first.
            // A child with no frames (nothing captured) goes last, in walk order.
            _rootKids.Sort(ByFrameStart);
            foreach (var kids in _kids)
            {
                kids?.Sort(ByFrameStart);
            }
        }

        private int ByFrameStart(int a, int b)
        {
            var sa = _capture.Items[a].FrameStart;
            var sb = _capture.Items[b].FrameStart;
            var ka = sa < 0 ? int.MaxValue : sa;
            var kb = sb < 0 ? int.MaxValue : sb;
            return ka != kb ? ka.CompareTo(kb) : a.CompareTo(b);
        }

        internal DevToolsComponentNode Build(Component root)
        {
            _budget--;
            var children = new List<DevToolsComponentNode>();
            var frames = _capture.Frames;
            if (_capture.FrameCount >= 0)
            {
                var ordinal = 0;
                var next = 0;
                AddRange(children, 0, frames.Length, _rootKids, ref next, _owner.IdOf(root), ref ordinal, claimAtEnd: true);
            }
            else
            {
                AddComponents(children, _rootKids);
            }

            // The root rendered the whole document, which is no box anyone hovers for.
            return Describe(_owner.IdOf(root), root, children, at: null);
        }

        private DevToolsComponentNode BuildComponent(int index)
        {
            _budget--;
            var item = _capture.Items[index];
            var id = _owner.IdOf(item.Component);
            var kids = _kids[index] ?? [];
            var children = new List<DevToolsComponentNode>();

            if (_capture.FrameCount >= 0 && item.FrameStart >= 0 && item.FrameEnd <= _capture.FrameCount
                && item.FrameStart <= item.FrameEnd)
            {
                var ordinal = 0;
                var next = 0;
                AddRange(children, item.FrameStart, item.FrameEnd, kids, ref next, id, ref ordinal, claimAtEnd: true);
            }
            else
            {
                AddComponents(children, kids);
            }

            return Describe(id, item.Component, children, Locate(item.FrameStart, item.FrameEnd));
        }

        // Through FramePathWalker, the same slot arithmetic the diff uses, so the box drawn is around the nodes the diff
        // would patch. It walks only the levels on the way to the span, skipping whole subtrees by their length.
        private string? Locate(int start, int end)
        {
            if (_capture.FrameCount < 0
                || !FramePathWalker.TryResolve(_capture.Frames, start, end, _path, out var first, out var count)
                || count == 0)
            {
                return null;
            }

            return string.Join('.', _path) + "|" + first.ToString(CultureInfo.InvariantCulture) + "|"
                   + count.ToString(CultureInfo.InvariantCulture);
        }

        private void AddComponents(List<DevToolsComponentNode> into, List<int> kids)
        {
            foreach (var kid in kids)
            {
                if (_budget <= 0)
                {
                    return;
                }

                into.Add(BuildComponent(kid));
            }
        }

        // Walks one level of the frame stream from `from` to `to`: an element becomes a tag whose children are the level
        // inside it, and a child component whose span starts here becomes that component. `next` is shared down the
        // recursion, because a component's children are met in page order wherever in its elements they sit.
        private void AddRange(
            List<DevToolsComponentNode> into, int from, int to, List<int> kids, ref int next, long ownerId, ref int ordinal,
            bool claimAtEnd)
        {
            var frames = _capture.Frames;
            var i = from;
            while (_budget > 0)
            {
                // A child that starts at or before this point is next on the page. One that starts exactly at `to` renders
                // nothing and is claimed by the component's own level, not by the last element before it.
                if (next < kids.Count)
                {
                    var start = _capture.Items[kids[next]].FrameStart;
                    if (start <= i && (start < to || claimAtEnd))
                    {
                        into.Add(BuildComponent(kids[next]));
                        i = Math.Max(i, _capture.Items[kids[next]].FrameEnd);
                        next++;
                        continue;
                    }
                }

                if (i >= to)
                {
                    break;
                }

                ref readonly var frame = ref frames[i];
                if (frame.Kind != RenderFrameKind.Element)
                {
                    i++;
                    continue;
                }

                _budget--;
                var length = Math.Max(1, frame.SubtreeLength);
                var tagId = (ownerId << TagBits) | (uint)(++ordinal & ((1 << TagBits) - 1));
                var children = new List<DevToolsComponentNode>();
                AddRange(children, i + 1, Math.Min(to, i + length), kids, ref next, ownerId, ref ordinal, claimAtEnd: false);
                into.Add(new DevToolsComponentNode(
                    tagId, frame.Name ?? "?", null, [], children, IsTag: true, At: Locate(i, i + length)));
                i += length;
            }

            // A child whose span the frames could not place — one that threw half way, or a stream the walk rewound — is
            // still a child: shown after the rest rather than lost.
            if (claimAtEnd)
            {
                while (next < kids.Count && _budget > 0)
                {
                    into.Add(BuildComponent(kids[next]));
                    next++;
                }
            }
        }
    }
}
