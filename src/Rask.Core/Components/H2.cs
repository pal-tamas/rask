namespace Rask.Core.Components;

/// <summary>
///     A second-level heading — a major section of the page. Never skip a level to get a smaller heading;
///     style it with CSS instead.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/Heading_Elements">MDN</see>
/// </summary>
public sealed class H2 : HtmlHeadingElement
{
    protected override string TagName => "h2";
}
