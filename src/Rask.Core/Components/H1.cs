namespace Rask.Core.Components;

/// <summary>
///     The top-level heading. One per page as a rule: it names the document, and assistive technology uses
///     the heading levels as the page's outline.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/Heading_Elements">MDN</see>
/// </summary>
public sealed class H1 : HtmlHeadingElement
{
    protected override string TagName => "h1";
}
