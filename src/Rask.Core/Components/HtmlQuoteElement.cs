using System.Text;

namespace Rask.Core.Components;

// Shared base for the quote elements (Q, Blockquote), mirroring the DOM `HTMLQuoteElement`
// interface. Both carry the URL-sanitized `cite` attribute and add nothing else.

/// <summary>
///     The attribute <c>blockquote</c> and <c>q</c> share. Not a tag of its own. <see
///     href="https://developer.mozilla.org/en-US/docs/Web/API/HTMLQuoteElement">MDN: HTMLQuoteElement</see>
/// </summary>
public abstract class HtmlQuoteElement : Element
{
    /// <summary>
    ///     A URL for the source of the quotation. Not displayed by browsers — surface it in your own markup
    ///     if readers need it.
    /// </summary>
    public new string? Cite { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cite is not null)
        {
            AppendUrlAttr(sb, "cite", Cite);
        }
    }
}
