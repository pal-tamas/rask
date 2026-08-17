namespace Rask.Html.Components;

// Shared base for the table-section elements (Thead, Tbody, Tfoot), mirroring the DOM
// `HTMLTableSectionElement` interface. It carries no extra attributes over HTMLElement, so this
// base adds none either — it exists purely for structural fidelity.
public abstract partial class HtmlTableSectionElement : Element
{
}
