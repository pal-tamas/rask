using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     The document's base URL and default browsing context for every relative link. At most one per
///     document, and it must precede any URL it is meant to affect.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/base">MDN</see>
/// </summary>
public sealed partial class Base : Element
{
    protected override string TagName => "base";
    protected override bool SelfClosing => true;

    /// <summary>The base URL that relative URLs in the document resolve against.</summary>
    public string? Href { get; set; }

    /// <summary>The default <c>target</c> for every link and form in the document.</summary>
    public string? Target { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (Target is not null)
        {
            AppendAttr(sb, "target", Target);
        }
    }
}
