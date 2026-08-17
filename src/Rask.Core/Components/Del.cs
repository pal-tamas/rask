namespace Rask.Core.Components;

/// <summary>
///     Text removed from the document. <c>Cite</c> and <c>DateTime</c> record why and when. Pairs with
///     <c>ins</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/del">MDN</see>
/// </summary>
public sealed class Del : HtmlModElement
{
    protected override string TagName => "del";
}
