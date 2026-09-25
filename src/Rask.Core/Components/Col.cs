namespace Rask.Core.Components;

/// <summary>
///     Styling and attributes for one or more table columns. Must sit inside a <c>colgroup</c>. Only a few
///     CSS properties apply to a column — border, background, width and visibility.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/col">MDN</see>
/// </summary>
[Tag("col")]
public sealed partial class Col : HtmlTableColElement
{
}
