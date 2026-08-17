namespace Rask.Html.Components;

/// <summary>
///     The table's summary rows — totals and the like.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/tfoot">MDN</see>
/// </summary>
public sealed partial class Tfoot : HtmlTableSectionElement
{
    protected override string TagName => "tfoot";
}
