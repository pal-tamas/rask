namespace Rask.Html.Components;

/// <summary>
///     The table's body rows, as distinct from its header and footer. A table may have several, each
///     grouping related rows.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/tbody">MDN</see>
/// </summary>
public sealed partial class Tbody : HtmlTableSectionElement
{
    protected override string TagName => "tbody";
}
