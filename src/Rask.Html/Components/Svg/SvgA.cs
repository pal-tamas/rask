using System.Text;

namespace Rask.Html.Components;

// SVG <a> hyperlink. Named SvgA to avoid colliding with the HTML A component.

/// <summary>
///     A hyperlink around SVG content. Named <c>SvgA</c> so it does not collide with the HTML <c>a</c>; it
///     still renders as <c>a</c>, inside the SVG namespace.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/a">MDN</see>
/// </summary>
public sealed partial class SvgA : SvgElement
{
    protected override string TagName => "a";

    /// <summary>Where the link goes.</summary>
    public string? Href { get; set; }

    /// <summary>Which browsing context opens the link.</summary>
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
