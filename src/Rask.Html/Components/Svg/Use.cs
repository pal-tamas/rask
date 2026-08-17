using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     Draws another element again somewhere else, referenced by <c>Href</c>. The way to reuse an icon
///     defined once in a <c>defs</c> or a <c>symbol</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/use">MDN</see>
/// </summary>
public sealed partial class Use : SvgElement
{
    protected override string TagName => "use";

    /// <summary>The element to clone, as <c>#id</c>. Cross-origin references are not allowed.</summary>
    public string? Href { get; set; }

    /// <summary>The x offset to draw the clone at.</summary>
    public string? X { get; set; }

    /// <summary>The y offset to draw the clone at.</summary>
    public string? Y { get; set; }

    /// <summary>
    ///     The clone's width. Applies only when the referenced element is an <c>svg</c> or <c>symbol</c>.
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    ///     The clone's height. Applies only when the referenced element is an <c>svg</c> or <c>symbol</c>.
    /// </summary>
    public string? Height { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (X is not null)
        {
            AppendAttr(sb, "x", X);
        }

        if (Y is not null)
        {
            AppendAttr(sb, "y", Y);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width);
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height);
        }
    }
}
