namespace Rask.Html.Components;

/// <summary>
///     A self-contained composition that would still make sense republished on its own — a post, a comment,
///     a product card. Give it a heading.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/article">MDN</see>
/// </summary>
public sealed partial class Article : Element
{
    protected override string TagName => "article";
}
