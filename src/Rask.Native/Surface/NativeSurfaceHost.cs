namespace Rask.Native.Surface;

/// <summary>
///     Holds a surface backend's retained view tree and replays <see cref="NativePatch" />es against it. Shared
///     by every platform: a head supplies an <see cref="INativeViewOps{TView}" /> mapping table and gets
///     mounting, path resolution and ordered child edits for free.
/// </summary>
/// <typeparam name="TView">The platform's view type.</typeparam>
/// <remarks>
///     Patches are applied strictly in order, each against the tree as the previous ones left it — that is the
///     contract <c>NativeTreeDiffer</c> emits against, and the reason a child op carries a plain index rather
///     than an identity. Getting that wrong scrambles a screen in ways that are hard to see in a patch list,
///     which is why this lives here, on plain <c>net10.0</c>, under unit test.
/// </remarks>
public sealed class NativeSurfaceHost<TView>(INativeViewOps<TView> ops)
{
    private readonly INativeViewOps<TView> _ops = ops;
    private Node? _root;

    /// <summary>The root view of the mounted tree, or <c>default</c> when nothing has been mounted yet.</summary>
    public TView? RootView => _root is null ? default : _root.View;

    /// <summary>Whether a tree is currently mounted.</summary>
    public bool IsMounted => _root is not null;

    /// <summary>Builds <paramref name="root" />'s whole subtree from scratch and makes it the retained tree.</summary>
    /// <returns>The new root view.</returns>
    public TView Mount(NativeNode root)
    {
        _root = Build(root);
        return _root.View;
    }

    /// <summary>Replays <paramref name="patches" /> against the retained tree, in order.</summary>
    /// <exception cref="InvalidOperationException">Nothing has been mounted yet.</exception>
    public void Apply(IReadOnlyList<NativePatch> patches)
    {
        if (_root is null)
        {
            throw new InvalidOperationException(
                "A native surface received patches before anything was mounted. The session mounts first and "
                + "patches afterwards, so this means the backend dropped a mount.");
        }

        foreach (var patch in patches)
        {
            ApplyOne(patch);
        }
    }

    private void ApplyOne(NativePatch patch)
    {
        switch (patch.Kind)
        {
            case NativePatchKind.SetProps:
            {
                var target = Resolve(patch.Path);
                foreach (var prop in patch.Props ?? [])
                {
                    _ops.SetProp(target.View, target.Kind, prop.Id, prop.Value);
                }

                break;
            }

            case NativePatchKind.Replace:
            {
                var replacement = Build(patch.Node!);
                if (patch.Path.Length == 0)
                {
                    // A root whose kind changed cannot be patched in place; the differ says so by returning
                    // null and the session re-mounts, so this only ever runs for a same-kind root.
                    _root = replacement;
                    break;
                }

                var parent = Resolve(patch.Path.AsSpan(0, patch.Path.Length - 1));
                var index = patch.Path[^1];
                var old = parent.Children[index];
                _ops.RemoveChild(parent.View, parent.Kind, old.View, index);
                parent.Children[index] = replacement;
                _ops.InsertChild(parent.View, parent.Kind, replacement.View, index);
                break;
            }

            case NativePatchKind.Insert:
            {
                var parent = Resolve(patch.Path);
                var child = Build(patch.Node!);
                parent.Children.Insert(patch.Index, child);
                _ops.InsertChild(parent.View, parent.Kind, child.View, patch.Index);
                break;
            }

            case NativePatchKind.Remove:
            {
                var parent = Resolve(patch.Path);
                var child = parent.Children[patch.Index];
                parent.Children.RemoveAt(patch.Index);
                _ops.RemoveChild(parent.View, parent.Kind, child.View, patch.Index);
                break;
            }

            case NativePatchKind.Move:
            {
                var parent = Resolve(patch.Path);
                var child = parent.Children[patch.FromIndex];
                parent.Children.RemoveAt(patch.FromIndex);
                parent.Children.Insert(patch.Index, child);
                _ops.MoveChild(parent.View, parent.Kind, child.View, patch.FromIndex, patch.Index);
                break;
            }

            default:
                throw new InvalidOperationException($"Unknown native patch kind '{patch.Kind}'.");
        }
    }

    private Node Resolve(ReadOnlySpan<int> path)
    {
        var node = _root!;
        foreach (var index in path)
        {
            node = node.Children[index];
        }

        return node;
    }

    private Node Build(NativeNode source)
    {
        var view = _ops.Create(source.Kind);
        foreach (var prop in source.Props)
        {
            _ops.SetProp(view, source.Kind, prop.Id, prop.Value);
        }

        var node = new Node(source.Kind, view);
        for (var i = 0; i < source.Children.Length; i++)
        {
            var child = Build(source.Children[i]);
            node.Children.Add(child);
            _ops.InsertChild(view, source.Kind, child.View, i);
        }

        return node;
    }

    // The retained mirror of the node tree. It exists because a patch addresses a node by PATH, and a
    // platform's own child collection is not always the right thing to walk (a UIStackView's arranged
    // subviews are a different list from its subviews, and a scroll view wraps its content in another view).
    private sealed class Node(NativeNodeKind kind, TView view)
    {
        public NativeNodeKind Kind { get; } = kind;
        public TView View { get; } = view;
        public List<Node> Children { get; } = [];
    }
}
