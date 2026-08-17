namespace Rask.Core.Components;

/// <summary>
///     Introductory content for its nearest sectioning ancestor — a masthead, a logo, a search box, the top
///     of an article.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/header">MDN</see>
/// </summary>
public sealed class Header : Element
{
    protected override string TagName => "header";
}
