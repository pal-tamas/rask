using System.Text;

namespace Rask.Html.Components;

// SVG <text>. Named SvgText to avoid colliding with the Text component (a text node).

/// <summary>
///     Text drawn as part of the graphic — selectable, searchable and readable by assistive technology,
///     unlike text baked into a path. Named <c>SvgText</c> so it does not collide with Rask's <c>Text</c>
///     primitive.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/text">MDN</see>
/// </summary>
public sealed partial class SvgText : SvgTextPositioningElement
{
    protected override string TagName => "text";

    /// <summary>The x coordinate of the text's starting point.</summary>

    /// <summary>The y coordinate of the text's baseline.</summary>

    /// <summary>A horizontal shift from the current position.</summary>

    /// <summary>A vertical shift from the current position.</summary>

    /// <summary>Per-glyph rotation in degrees; the last value applies to every remaining glyph.</summary>


    /// <summary>
    ///     Which baseline aligns with <c>Y</c> — <c>middle</c> and <c>central</c> are how you vertically
    ///     centre a label.
    /// </summary>
    public string? DominantBaseline { get; set; }

    /// <summary>The font family, as in CSS.</summary>
    public string? FontFamily { get; set; }

    /// <summary>The font size, as in CSS.</summary>
    public string? FontSize { get; set; }

    /// <summary>The font weight, as in CSS.</summary>
    public string? FontWeight { get; set; }


    /// <summary>The exact width the text must occupy; the browser adjusts spacing to make it fit.</summary>

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);






        if (DominantBaseline is not null)
        {
            AppendAttr(sb, "dominant-baseline", DominantBaseline);
        }

        if (FontFamily is not null)
        {
            AppendAttr(sb, "font-family", FontFamily);
        }

        if (FontSize is not null)
        {
            AppendAttr(sb, "font-size", FontSize);
        }

        if (FontWeight is not null)
        {
            AppendAttr(sb, "font-weight", FontWeight);
        }


    }
}
