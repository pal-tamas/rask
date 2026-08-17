using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A vertically scrolling list of rows. Each child is one row — usually a <see cref="NativeStack" />, which
///     can carry its own <c>OnClick</c> to make the row selectable.
/// </summary>
/// <remarks>
///     <para>
///         <b>Rows are not recycled.</b> The backend builds every row as a real view and keeps it, so this
///         suits the tens-of-rows lists most screens have, not thousands. Cell reuse needs the platform's
///         recycling collection (<c>UITableView</c> / <c>RecyclerView</c>), whose data-source model does not
///         fit a patch-addressed tree; wiring one up is a follow-up.
///     </para>
///     <b>Give every row a <c>Key</c>.</b> Keyed rows reconcile by identity, so inserting, removing or
///     reordering moves the existing row views instead of rewriting each one's contents in place — which is
///     what keeps scroll position, focus and in-flight animations intact. Without keys the rows match by
///     position, and reordering repaints all of them.
/// </remarks>
/// <example>
///     <code>NativeList()[.. todos.Select(t =>
///         NativeStack.Key(t.Id).OnClick(() => Toggle(t))[NativeLabel[t.Title]])]</code>
/// </example>
public sealed partial class NativeList : NativeViewComponent
{
    /// <summary>The list's background color. Leave <c>null</c> for the platform's grouped-list background.</summary>
    public NativeColor? Background { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.List;

    /// <inheritdoc />
    internal override bool AcceptsChildren => true;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props) =>
        props.Color(NativePropId.Background, Background);
}
