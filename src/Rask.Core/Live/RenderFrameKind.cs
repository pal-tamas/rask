namespace Rask.Core.Live;

/// <summary>
///     Tag distinguishing the role of a frame in a render-tree stream. The stream is
///     emitted by <see cref="HtmlSerializer.Serialize(Component, System.Text.StringBuilder)" />
///     alongside the rendered HTML and consumed by <see cref="FrameDiffer" /> when
///     comparing successive renders to emit a minimal edit-op payload.
/// </summary>
public enum RenderFrameKind : byte
{
    /// <summary>
    ///     Opens an HTML element. The matching closing tag is implicit at
    ///     <c>index + SubtreeLength</c>; there is no <c>CloseElement</c> frame.
    /// </summary>
    Element = 1,

    /// <summary>A single name/value attribute on the most-recently-opened element.</summary>
    Attribute = 2,

    /// <summary>
    ///     HTML-encoded text content (the producer is responsible for any encoding
    ///     decisions — the frame stores the raw user-supplied text so a consumer can
    ///     re-encode if it writes to a different sink).
    /// </summary>
    Text = 3,

    /// <summary>Verbatim markup (no encoding). Corresponds to <see cref="Rask.Core.Components.Raw" />.</summary>
    Raw = 4,

    /// <summary>Doctype declaration.</summary>
    Doctype = 5,

    /// <summary>
    ///     Marks the start of a user-component's rendered subtree. The component
    ///     instance reference lets the diff codec short-circuit when an unchanged component
    ///     instance still produces an identical cached subtree.
    /// </summary>
    Component = 6
}
