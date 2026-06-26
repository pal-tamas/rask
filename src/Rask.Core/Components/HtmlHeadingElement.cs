namespace Rask.Core.Components;

// Shared base for the section-heading elements (H1-H6), mirroring the DOM `HTMLHeadingElement`
// interface. The DOM groups all six headings under one interface; it carries no extra attributes
// over HTMLElement, so this base adds none either — it exists purely for structural fidelity.
public abstract class HtmlHeadingElement : Element
{
}
