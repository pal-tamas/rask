using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A raster or SVG image placed inside a graphic. Unlike HTML's <c>img</c>, it is positioned in user
///     coordinates and participates in SVG transforms and clipping.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/image">MDN</see>
/// </summary>
public sealed partial class Image : SvgElement
{
    protected override string TagName => "image";

    /// <summary>The left edge's x coordinate.</summary>
    public string? X { get; set; }

    /// <summary>The top edge's y coordinate.</summary>
    public string? Y { get; set; }

    /// <summary>The rendered width. Required — an image with no width is not rendered.</summary>
    public string? Width { get; set; }

    /// <summary>The rendered height. Required — an image with no height is not rendered.</summary>
    public string? Height { get; set; }

    /// <summary>The image's URL.</summary>
    public string? Href { get; set; }

    /// <summary>How to fit the image into the given box when the aspect ratios differ.</summary>
    public string? PreserveAspectRatio { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
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

        if (Href is not null)
        {
            AppendMediaUrlAttr(sb, "href", Href);
        }

        if (PreserveAspectRatio is not null)
        {
            AppendAttr(sb, "preserveAspectRatio", PreserveAspectRatio);
        }
    }
}
