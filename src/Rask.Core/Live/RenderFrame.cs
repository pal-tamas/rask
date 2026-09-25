using System.Buffers;

namespace Rask.Core.Live;

/// <summary>
///     Compact tagged-union over the render-tree frame variants. One value type per
///     element / attribute / text / component-marker; consumers walk a contiguous
///     <see cref="Span{T}" /> of frames and use <see cref="SubtreeLength" /> on
///     <see cref="RenderFrameKind.Element" /> / <see cref="RenderFrameKind.Component" />
///     frames to skip subtrees without recursion. Modelled on Blazor's
///     <c>RenderTreeFrame</c> but trimmed to the variants Rask actually diffs.
/// </summary>
public struct RenderFrame
{
    public RenderFrameKind Kind { get; set; }

    /// <summary>
    ///     For <see cref="RenderFrameKind.Element" /> and
    ///     <see cref="RenderFrameKind.Component" />: total frames in the subtree rooted at
    ///     this frame including itself. <c>1</c> means a leaf element with no children. The
    ///     field is patched in by <see cref="FrameWriter.CloseElement" /> /
    ///     <see cref="FrameWriter.CloseComponent" /> at close time; while the element is
    ///     still open the value is meaningless.
    /// </summary>
    public int SubtreeLength { get; set; }

    /// <summary>
    ///     Element tag (for <see cref="RenderFrameKind.Element" />), attribute name
    ///     (for <see cref="RenderFrameKind.Attribute" />), or text content (for
    ///     <see cref="RenderFrameKind.Text" /> / <see cref="RenderFrameKind.Raw" />).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     For <see cref="RenderFrameKind.Attribute" />: the attribute value.
    ///     For <see cref="RenderFrameKind.Element" />: the active scoped-CSS id when the
    ///     element opened (or null when no scope is active), so consumers that emit edit-ops
    ///     for inserted elements can re-stamp <c>data-{scopeId}</c> client-side.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    ///     For <see cref="RenderFrameKind.Component" />: the component instance.
    ///     Allows the diff codec to compare by identity, letting cached subtrees
    ///     short-circuit a full frame walk.
    /// </summary>
    public Component? ComponentRef { get; set; }

    /// <summary>
    ///     For <see cref="RenderFrameKind.Element" />: whether the tag is
    ///     self-closing (<c>&lt;br /&gt;</c>). Persisted on the frame so a consumer
    ///     rendering edit-ops to HTML doesn't need a void-element lookup table.
    /// </summary>
    public bool SelfClosing { get; set; }

    /// <summary>
    ///     For <see cref="RenderFrameKind.Element" />: whether everything below this element is owned
    ///     by a foreign renderer (see <c>Rask.External</c>). <see cref="FrameDiffer" /> compares such an
    ///     element's attributes and then skips its whole subtree by <see cref="SubtreeLength" />,
    ///     because those nodes belong to React/Lit/Blazor and are reconciled on their schedule, not
    ///     ours. Packs into the padding beside <see cref="SelfClosing" />, so the frame does not grow.
    /// </summary>
    public bool Opaque { get; set; }

    /// <summary>
    ///     UTF-16 character offset into the rendered HTML string at which this
    ///     frame's serialized output begins. Set by <see cref="FrameWriter" /> at
    ///     <c>Open*</c> time; the matching <see cref="HtmlEnd" /> is set at <c>Close*</c>
    ///     time. The diff codec uses <c>[HtmlStart..HtmlEnd]</c> as the HTML fragment to
    ///     ship with an <see cref="RenderFrameKind" />-bearing op (specifically
    ///     <see cref="EditOpKind.InsertSubtree" />) so the client interpreter can apply
    ///     structural changes without re-rendering on its own. Frames without a
    ///     meaningful HTML range (e.g. <see cref="RenderFrameKind.Attribute" />) leave
    ///     these as zero.
    /// </summary>
    public int HtmlStart { get; set; }

    /// <summary>Companion to <see cref="HtmlStart" />.</summary>
    public int HtmlEnd { get; set; }
}
