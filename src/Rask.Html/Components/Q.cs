namespace Rask.Html.Components;

/// <summary>
///     A short inline quotation. The browser supplies the quotation marks — do not type them as well.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/q">MDN</see>
/// </summary>
public sealed partial class Q : HtmlQuoteElement
{
    protected override string TagName => "q";
}
