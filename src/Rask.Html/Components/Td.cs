namespace Rask.Html.Components;

/// <summary>
///     A data cell in a table row.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/td">MDN</see>
/// </summary>
public sealed partial class Td : HtmlTableCellElement
{
    protected override string TagName => "td";
}
