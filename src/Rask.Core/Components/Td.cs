namespace Rask.Core.Components;

/// <summary>
///     A data cell in a table row.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/td">MDN</see>
/// </summary>
public sealed class Td : HtmlTableCellElement
{
    protected override string TagName => "td";
}
