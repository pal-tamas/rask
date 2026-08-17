namespace Rask.Core.Components;

/// <summary>
///     A group of table columns, defined either by its <c>Span</c> or by its <c>col</c> children. Must come
///     after any <c>caption</c> and before every row.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/colgroup">MDN</see>
/// </summary>
public sealed class Colgroup : HtmlTableColElement
{
    protected override string TagName => "colgroup";
}
