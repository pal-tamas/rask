using Rask.Native.Surface;

namespace Rask.Native.Tests.Infrastructure;

/// <summary>
///     A test double for <see cref="INativeSurface" />. It does what a real platform backend does — keeps a
///     retained tree and applies patches to it — so a test can assert on the tree the user would actually be
///     looking at, not merely on the patch list. A backend that mis-applies a patch produces a wrong tree here
///     too, which is the point: this is the executable spec the iOS and Android heads must match.
/// </summary>
internal sealed class FakeNativeSurface : INativeSurface
{
    /// <summary>Every call the session made, in order — <c>"web"</c>, <c>"mount"</c> or <c>"patch:N"</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The retained tree, kept in sync by applying every patch exactly as a real backend would.</summary>
    public MutableNode? Tree { get; private set; }

    /// <summary>Whether the WebView is the visible content right now.</summary>
    public bool ShowingWebView { get; private set; }

    /// <summary>How many times the tree was built from scratch — a switch must NOT increase this.</summary>
    public int MountCount { get; private set; }

    public Func<NativeSurfaceEvent, Task>? OnSurfaceEvent { get; set; }

    public ValueTask ShowWebViewAsync()
    {
        Calls.Add("web");
        ShowingWebView = true;
        return default;
    }

    public ValueTask MountAsync(NativeNode root)
    {
        Calls.Add("mount");
        MountCount++;
        ShowingWebView = false;
        Tree = MutableNode.From(root);
        return default;
    }

    public ValueTask PatchAsync(IReadOnlyList<NativePatch> patches)
    {
        Calls.Add($"patch:{patches.Count}");
        ShowingWebView = false;
        foreach (var patch in patches)
        {
            Apply(patch);
        }

        return default;
    }

    /// <summary>Raises a tap, exactly as a platform head would after the user touched the view.</summary>
    public Task TapAsync(int handlerId) =>
        OnSurfaceEvent?.Invoke(new NativeSurfaceEvent(handlerId, NativeSurfaceEventKind.Tap, null))
        ?? Task.CompletedTask;

    /// <summary>Raises a value change (a text field's new text, or a switch's new state).</summary>
    public Task ChangeAsync(int handlerId, string value) =>
        OnSurfaceEvent?.Invoke(new NativeSurfaceEvent(handlerId, NativeSurfaceEventKind.Change, value))
        ?? Task.CompletedTask;

    /// <summary>Finds the first node of a kind in the retained tree, depth-first, or <c>null</c>.</summary>
    public MutableNode? Find(NativeNodeKind kind) => Tree is null ? null : Find(Tree, kind);

    private static MutableNode? Find(MutableNode node, NativeNodeKind kind)
    {
        if (node.Kind == kind)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (Find(child, kind) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private void Apply(NativePatch patch)
    {
        var target = Resolve(patch.Path);
        switch (patch.Kind)
        {
            case NativePatchKind.SetProps:
                foreach (var prop in patch.Props!)
                {
                    if (prop.Value.Kind == NativePropKind.None)
                    {
                        target.Props.Remove(prop.Id);
                    }
                    else
                    {
                        target.Props[prop.Id] = prop.Value;
                    }
                }

                break;

            case NativePatchKind.Replace:
                var replacement = MutableNode.From(patch.Node!);
                if (patch.Path.Length == 0)
                {
                    Tree = replacement;
                    break;
                }

                Resolve(patch.Path[..^1]).Children[patch.Path[^1]] = replacement;
                break;

            case NativePatchKind.Insert:
                target.Children.Insert(patch.Index, MutableNode.From(patch.Node!));
                break;

            case NativePatchKind.Remove:
                target.Children.RemoveAt(patch.Index);
                break;

            case NativePatchKind.Move:
                var moved = target.Children[patch.FromIndex];
                target.Children.RemoveAt(patch.FromIndex);
                target.Children.Insert(patch.Index, moved);
                break;

            default:
                throw new InvalidOperationException($"Unhandled patch kind {patch.Kind}.");
        }
    }

    private MutableNode Resolve(ReadOnlySpan<int> path)
    {
        var node = Tree ?? throw new InvalidOperationException("A patch arrived before any mount.");
        foreach (var index in path)
        {
            node = node.Children[index];
        }

        return node;
    }

    /// <summary>A retained view: what a real backend holds on to between frames.</summary>
    internal sealed class MutableNode
    {
        public NativeNodeKind Kind { get; init; }
        public string? Key { get; init; }
        public Dictionary<NativePropId, NativePropValue> Props { get; } = [];
        public List<MutableNode> Children { get; } = [];

        public static MutableNode From(NativeNode node)
        {
            var copy = new MutableNode { Kind = node.Kind, Key = node.Key };
            foreach (var prop in node.Props)
            {
                copy.Props[prop.Id] = prop.Value;
            }

            foreach (var child in node.Children)
            {
                copy.Children.Add(From(child));
            }

            return copy;
        }

        /// <summary>The node's text prop, or <c>null</c> when it has none — the usual assertion target.</summary>
        public string? Text => Props.TryGetValue(NativePropId.Text, out var v) ? v.Text : null;
    }
}
