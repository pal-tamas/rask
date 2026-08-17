namespace Rask.Core.Components;

/// <summary>
///     Tabular data in rows and columns. For layout, use CSS grid or flexbox — a layout table is announced
///     to screen readers as data.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/table">MDN</see>
/// </summary>
public sealed class Table : Element
{
    protected override string TagName => "table";
}
