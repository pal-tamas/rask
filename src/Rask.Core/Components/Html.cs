using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     The document's root element. Everything else lives inside it, and setting <c>Lang</c> here is the
///     single highest-value accessibility attribute on the page.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/html">MDN</see>
/// </summary>
public sealed class Html : Element
{
    protected override string TagName => "html";

    /// <summary>
    ///     The document's language as a BCP 47 tag (<c>en</c>, <c>en-GB</c>, <c>hu</c>). Drives
    ///     screen-reader pronunciation, hyphenation and translation offers — set it on every page.
    /// </summary>
    public string? Lang { get; set; }

    /// <summary>The base text direction: <c>ltr</c>, <c>rtl</c>, or <c>auto</c>.</summary>
    public string? Dir { get; set; }

    /// <summary>The XML namespace. Needed only when the document is served as XHTML.</summary>
    public string? Xmlns { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Lang is not null)
        {
            AppendAttr(sb, "lang", Lang);
        }

        if (Dir is not null)
        {
            AppendAttr(sb, "dir", Dir);
        }

        if (Xmlns is not null)
        {
            AppendAttr(sb, "xmlns", Xmlns);
        }
    }
}
