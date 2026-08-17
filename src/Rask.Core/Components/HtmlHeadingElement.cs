namespace Rask.Core.Components;

// Shared base for the section-heading elements (H1-H6), mirroring the DOM `HTMLHeadingElement`
// interface. The DOM groups all six headings under one interface; it carries no extra attributes
// over HTMLElement, so this base adds none either — it exists purely for structural fidelity.

/// <summary>
///     The shared base of <c>h1</c>–<c>h6</c>. Not a tag of its own — the six heading levels differ only in
///     rank. <see href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLHeadingElement">MDN:
///     HTMLHeadingElement</see>
/// </summary>
public abstract class HtmlHeadingElement : Element
{
}
