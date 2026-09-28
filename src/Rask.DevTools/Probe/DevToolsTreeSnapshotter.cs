using System.Runtime.CompilerServices;
using Rask.Core;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;

namespace Rask.DevTools.Probe;

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


    /// <summary>The element an island or a Blazor component renders, named as the badge a developer knows it by.</summary>
    internal static string? BadgeOf(ReadOnlySpan<RenderFrame> frames, int start)
    {
        if (start < 0 || start >= frames.Length || frames[start].Kind != RenderFrameKind.Element)
        {
            return null;
        }

        switch (frames[start].Name)
        {
            case "rask-blazor":
                return "Blazor";
            case "rask-external":
                for (var i = start + 1; i < frames.Length && frames[i].Kind == RenderFrameKind.Attribute; i++)
                {
                    if (string.Equals(frames[i].Name, "runtime", StringComparison.Ordinal) && frames[i].Value is { Length: > 0 } runtime)
                    {
                        return runtime switch
                        {
                            "react" => "React",
                            "preact" => "Preact",
                            "solid" => "Solid",
                            "vue" => "Vue",
                            "svelte" => "Svelte",
                            "angular" => "Angular",
                            "lit" => "Lit",
                            _ => runtime,
                        };
                    }
                }

                return "Island";
            default:
                return null;
        }
    }

    private sealed class Builder
    {
        private readonly DevToolsTreeSnapshotter _owner;
        private readonly DevToolsTreeCapture _capture;
        private readonly Dictionary<Component, int> _index = new(ReferenceEqualityComparer.Instance);
        private readonly List<int>?[] _kids;
        private readonly List<int> _rootKids = [];
        private readonly List<int> _path = [];
        private readonly Dictionary<Component, List<int>>? _providesBy;
        private readonly Dictionary<Component, List<int>>? _readsBy;
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

            Link(items, root);

            if (capture.HasContexts)
            {
                _providesBy = new Dictionary<Component, List<int>>(ReferenceEqualityComparer.Instance);
                for (var i = 0; i < capture.Provides.Length; i++)
                {
                    Add(_providesBy, capture.Provides[i].Owner, i);
                }

                _readsBy = new Dictionary<Component, List<int>>(ReferenceEqualityComparer.Instance);
                for (var i = 0; i < capture.Reads.Length; i++)
                {
                    Add(_readsBy, capture.Reads[i].Reader, i);
                }
            }
        }

        // A parent the walk never reported, or the root itself, makes a child of the root.
        private void Link(ReadOnlySpan<DevToolsWalkItem> items, Component? root)
        {
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

        private static void Add(Dictionary<Component, List<int>> by, Component component, int index)
        {
            if (!by.TryGetValue(component, out var list))
            {
                by[component] = list = [];
            }

            list.Add(index);
        }

        private DevToolsComponentNode Describe(
            long id, Component component, List<DevToolsComponentNode> children, string? at, string? badge = null)
        {
            // What the component says about itself. The override the build wrote reads its own properties by name; in a
            // build without the devtools the base method is empty, so this is a call that collects nothing.
            var describer = new PropsDescriber();
            component.DescribeProps(describer);
            return new DevToolsComponentNode(
                id, DevToolsNames.Of(component.GetType()), component.Key?.ToString(), describer.Props, children, At: at,
                Badge: badge, Provides: ProvidesOf(component), Reads: ReadsOf(component));
        }

        private DevToolsProvidedContext[]? ProvidesOf(Component component)
        {
            if (_providesBy is null)
            {
                return null;
            }

            if (!_providesBy.TryGetValue(component, out var indexes))
            {
                return [];
            }

            var provided = new DevToolsProvidedContext[indexes.Count];
            for (var i = 0; i < indexes.Count; i++)
            {
                provided[i] = DevToolsSensitive.Describe(_capture.Provides[indexes[i]]);
            }

            return provided;
        }

        // Once per type and name: a component that reads the same value twice in one render read one thing.
        private List<DevToolsReadContext>? ReadsOf(Component component)
        {
            if (_readsBy is null)
            {
                return null;
            }

            if (!_readsBy.TryGetValue(component, out var indexes))
            {
                return [];
            }

            var reads = new List<DevToolsReadContext>(indexes.Count);
            var seen = new HashSet<(Type, string?)>();
            foreach (var index in indexes)
            {
                var read = _capture.Reads[index];
                if (!seen.Add((read.Requested, read.Name)))
                {
                    continue;
                }

                reads.Add(new DevToolsReadContext(
                    DevToolsNames.Of(read.Requested), read.Name, read.Found,
                    read.Provider is { } provider ? _owner.IdOf(provider) : null,
                    read.Provider is { } named ? DevToolsNames.Of(named.GetType()) : null));
            }

            return reads;
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

            return Describe(
                id, item.Component, children, Locate(item.FrameStart, item.FrameEnd),
                _capture.FrameCount >= 0 ? BadgeOf(_capture.Frames, item.FrameStart) : null);
        }

        // Through FramePathWalker, the same slot arithmetic the diff uses, so the box drawn is around the nodes the diff
        // would patch. It walks only the levels on the way to the span, skipping whole subtrees by their length.
        private string? Locate(int start, int end) =>
            _capture.FrameCount < 0 ? null : DevToolsPlaces.Locate(_capture.Frames, start, end, _path);

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
