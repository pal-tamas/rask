namespace Rask.Core.Components;

/// <summary>
///     An extended quotation, set off as its own block. Attribution belongs outside the quote (in a
///     <c>figcaption</c>, typically), not inside it.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/blockquote">MDN</see>
/// </summary>
[Tag("blockquote")]
public sealed partial class Blockquote : HtmlQuoteElement
{
}
