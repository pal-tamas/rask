namespace Rask.Core.Components;

/// <summary>
///     A thematic grouping of content, normally with a heading. When the content stands alone use
///     <c>article</c>; when there is nothing thematic about the grouping, use <c>div</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/section">MDN</see>
/// </summary>
public sealed class Section : Element
{
    protected override string TagName => "section";
}
