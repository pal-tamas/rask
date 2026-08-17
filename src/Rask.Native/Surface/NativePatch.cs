namespace Rask.Native.Surface;

/// <summary>What a <see cref="NativePatch" /> does to the view tree.</summary>
public enum NativePatchKind
{
    /// <summary>
    ///     Rebuild the node at <see cref="NativePatch.Path" /> from <see cref="NativePatch.Node" />. Emitted
    ///     when the kind changed — a label cannot become a button in place.
    /// </summary>
    Replace,

    /// <summary>
    ///     Apply <see cref="NativePatch.Props" /> — only the props whose value actually changed — to the
    ///     existing view at <see cref="NativePatch.Path" />, leaving its children alone.
    /// </summary>
    SetProps,

    /// <summary>Insert <see cref="NativePatch.Node" /> as a new child at <see cref="NativePatch.Index" />.</summary>
    Insert,

    /// <summary>Remove the child at <see cref="NativePatch.Index" />.</summary>
    Remove,

    /// <summary>Move the child at <see cref="NativePatch.FromIndex" /> to <see cref="NativePatch.Index" />.</summary>
    Move,
}

/// <summary>
///     One edit against the surface's retained view tree. A frame's patches are ordered and must be applied in
///     order: each child-list op (<see cref="NativePatchKind.Insert" />, <see cref="NativePatchKind.Remove" />,
///     <see cref="NativePatchKind.Move" />) is expressed against the list state left by the ops before it, so a
///     backend can apply them with a plain remove-at / insert-at on its child collection.
/// </summary>
/// <remarks>
///     <see cref="Path" /> is the chain of child indices from the tree root — empty means the root itself — and
///     addresses the node the op applies TO (for the child-list ops, the PARENT whose children change). Paths
///     are resolved against the tree as it stands when the op runs, which is why order matters.
/// </remarks>
public sealed class NativePatch
{
    /// <summary>What this patch does.</summary>
    public required NativePatchKind Kind { get; init; }

    /// <summary>Child indices from the root to the target node; empty addresses the root.</summary>
    public required int[] Path { get; init; }

    /// <summary>The new subtree, for <see cref="NativePatchKind.Replace" /> and <see cref="NativePatchKind.Insert" />.</summary>
    public NativeNode? Node { get; init; }

    /// <summary>The changed props only, for <see cref="NativePatchKind.SetProps" />.</summary>
    public NativeProp[]? Props { get; init; }

    /// <summary>The target child index, for <see cref="NativePatchKind.Insert" />, <see cref="NativePatchKind.Remove" /> and <see cref="NativePatchKind.Move" />.</summary>
    public int Index { get; init; }

    /// <summary>The source child index, for <see cref="NativePatchKind.Move" />.</summary>
    public int FromIndex { get; init; }
}
