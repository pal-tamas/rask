using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A tile repeated across a fill or stroke. Reference it as <c>Fill: "url(#id)"</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/pattern">MDN</see>
/// </summary>
public sealed class Pattern : SvgElement
{
    protected override string TagName => "pattern";

    /// <summary>The tile's x offset.</summary>
    public string? X { get; set; }

    /// <summary>The tile's y offset.</summary>
    public string? Y { get; set; }

    /// <summary>The tile's width. Zero disables rendering of the element using it.</summary>
    public string? Width { get; set; }

    /// <summary>The tile's height. Zero disables rendering of the element using it.</summary>
    public string? Height { get; set; }

    /// <summary>
    ///     The coordinate system for the tile's position and size: <c>objectBoundingBox</c> (the default)
    ///     or <c>userSpaceOnUse</c>.
    /// </summary>
    public string? PatternUnits { get; set; }

    /// <summary>The coordinate system for the tile's contents.</summary>
    public string? PatternContentUnits { get; set; }

    /// <summary>A transform applied to the pattern as a whole.</summary>
    public string? PatternTransform { get; set; }

    /// <summary>A user-coordinate rectangle mapped onto the tile.</summary>
    public string? ViewBox { get; set; }

    /// <summary>How to fit the <c>ViewBox</c> into the tile.</summary>
    public string? PreserveAspectRatio { get; set; }

    /// <summary>Another pattern to inherit attributes and content from.</summary>
    public string? Href { get; set; }

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

        if (PatternUnits is not null)
        {
            AppendAttr(sb, "patternUnits", PatternUnits);
        }

        if (PatternContentUnits is not null)
        {
            AppendAttr(sb, "patternContentUnits", PatternContentUnits);
        }

        if (PatternTransform is not null)
        {
            AppendAttr(sb, "patternTransform", PatternTransform);
        }

        if (ViewBox is not null)
        {
            AppendAttr(sb, "viewBox", ViewBox);
        }

        if (PreserveAspectRatio is not null)
        {
            AppendAttr(sb, "preserveAspectRatio", PreserveAspectRatio);
        }

        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }
    }
}
