namespace Rask.Native.Surface;

/// <summary>
///     The only platform-specific half of a surface backend: how to make one view, how to configure it, and
///     how to reorder a container's children. Everything else — holding the retained tree, resolving a patch's
///     path, replaying inserts/removes/moves in order — is
///     <see cref="NativeSurfaceHost{TView}" />'s job and is shared by every platform.
/// </summary>
/// <typeparam name="TView">The platform's view type: <c>UIView</c> on iOS, <c>android.view.View</c> on Android.</typeparam>
/// <remarks>
///     Splitting it here is what makes the risky part testable off-device. Patch replay is where a subtle
///     ordering bug would show up as a scrambled screen, and it now runs on plain <c>net10.0</c> against a
///     fake view type in the unit suite; a platform head only has to get its mapping table right.
///     <para>
///         Every method is called on the UI thread — <see cref="NativeSurfaceHost{TView}" /> does no
///         marshalling of its own, because the backend that owns the thread is better placed to batch it.
///     </para>
/// </remarks>
public interface INativeViewOps<TView>
{
    /// <summary>Creates an empty view for <paramref name="kind" />, before any props are applied.</summary>
    TView Create(NativeNodeKind kind);

    /// <summary>
    ///     Applies one property. A value whose <see cref="NativePropValue.Kind" /> is
    ///     <see cref="NativePropKind.None" /> means the prop went away and the view must go back to its
    ///     platform default for it.
    /// </summary>
    /// <param name="view">The view to configure.</param>
    /// <param name="kind">What the view is — the same prop id can mean different things per kind.</param>
    /// <param name="id">Which property.</param>
    /// <param name="value">Its new value, or <see cref="NativePropValue.Unset" /> to reset it.</param>
    void SetProp(TView view, NativeNodeKind kind, NativePropId id, NativePropValue value);

    /// <summary>Inserts <paramref name="child" /> into <paramref name="parent" /> at <paramref name="index" />.</summary>
    void InsertChild(TView parent, NativeNodeKind parentKind, TView child, int index);

    /// <summary>Removes the child at <paramref name="index" /> from <paramref name="parent" />.</summary>
    void RemoveChild(TView parent, NativeNodeKind parentKind, TView child, int index);

    /// <summary>
    ///     Moves an existing child from <paramref name="fromIndex" /> to <paramref name="toIndex" />. A
    ///     platform that has no direct move re-inserts, which is still cheaper than rebuilding the row.
    /// </summary>
    void MoveChild(TView parent, NativeNodeKind parentKind, TView child, int fromIndex, int toIndex);
}
