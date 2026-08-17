namespace Rask.Core.Components;

/// <summary>
///     Text added to the document. <c>Cite</c> and <c>DateTime</c> record why and when. Pairs with
///     <c>del</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/ins">MDN</see>
/// </summary>
public sealed class Ins : HtmlModElement
{
    protected override string TagName => "ins";
}
