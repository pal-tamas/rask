namespace Rask.Html.Components;

// Shared base for the table-section elements (Thead, Tbody, Tfoot), mirroring the DOM
// `HTMLTableSectionElement` interface. It carries no extra attributes over HTMLElement, so this
// base adds none either — it exists purely for structural fidelity.

/// <summary>
///     The shared base of <c>thead</c>, <c>tbody</c> and <c>tfoot</c> — the row-group sections of a table.
///     Not a tag of its own. <see
///     href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLTableSectionElement">MDN:
///     HTMLTableSectionElement</see>
/// </summary>
public abstract partial class HtmlTableSectionElement : Element
{
}
