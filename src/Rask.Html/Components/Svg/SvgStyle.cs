using System.Text;

namespace Rask.Html.Components;

// SVG <style>. Named SvgStyle to avoid colliding with the HTML Style component.

/// <summary>
///     CSS scoped to the SVG document. Named <c>SvgStyle</c> so it does not collide with the HTML
///     <c>style</c> element or with the universal <c>Style</c> attribute.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/style">MDN</see>
/// </summary>
public sealed partial class SvgStyle : SvgElement
{
    protected override string TagName => "style";

    /// <summary>The stylesheet language. Omit it — the only valid value is the default.</summary>
    public string? Type { get; set; }

    /// <summary>A media query restricting when the styles apply.</summary>
    public string? Media { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }
    }
}
