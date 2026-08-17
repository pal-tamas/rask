namespace Rask.Core.Components;

/// <summary>
///     A short inline quotation. The browser supplies the quotation marks — do not type them as well.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/q">MDN</see>
/// </summary>
public sealed class Q : HtmlQuoteElement
{
    protected override string TagName => "q";
}
