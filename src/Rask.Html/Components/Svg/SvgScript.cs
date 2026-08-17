using System.Text;

namespace Rask.Html.Components;

// SVG <script>. Named SvgScript to avoid colliding with the HTML Script component.

/// <summary>
///     Script inside an SVG document. Named <c>SvgScript</c> so it does not collide with the HTML
///     <c>script</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/script">MDN</see>
/// </summary>
public sealed partial class SvgScript : SvgElement
{
    protected override string TagName => "script";

    /// <summary>The URL of an external script.</summary>
    public string? Href { get; set; }

    /// <summary>The script's MIME type.</summary>
    public string? Type { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }
    }
}
