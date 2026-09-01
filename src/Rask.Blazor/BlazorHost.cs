using Rask.Core;

namespace Rask.Blazor;

/// <summary>The <c>&lt;rask-blazor&gt;</c> element a hosted component's markup is delivered inside.</summary>
/// <remarks>
///     <para>
///         A real <see cref="Element" /> rather than the island itself carrying the tag, and the
///         reason is the serializer's children fast path. <c>HtmlSerializer</c> walks
///         <c>ChildrenArray</c> directly and SKIPS <c>RenderChildren()</c> for any element that is
///         not opaque — and a statically rendered island must not be opaque, or the differ would skip
///         its children and the island would freeze after its first paint. So an island cannot both
///         be an element and rewrite its own children.
///     </para>
///     <para>
///         Splitting them resolves it without fighting either rule: the island is an ordinary
///         component, so its <c>Render()</c> is called normally, and what it returns is this element
///         holding the hosted markup. Everything downstream — diffing, morphing, head assets — treats
///         it as the plain element it is.
///     </para>
/// </remarks>
internal sealed partial class BlazorHost : Element
{
    /// <inheritdoc />
    protected override string TagName => BlazorDefaults.HostTag;

    /// <summary>Whether the subtree belongs to a Blazor circuit rather than to Rask.</summary>
    /// <remarks>
    ///     False for a statically rendered island: nothing in the browser owns those nodes, they are
    ///     Rask's own markup, and marking them opaque would make <c>FrameDiffer</c> skip the
    ///     subtree — so a prop change would render new HTML on the server and never ship it.
    /// </remarks>
    public bool Opaque { get; set; }

    /// <inheritdoc />
    protected override bool OpaqueSubtree => Opaque;
}
