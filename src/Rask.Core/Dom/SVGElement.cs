using System.Text;

namespace Rask.Core;

// What MDN's SVGElement (generated) keeps inline: the presentation attributes nearly every icon and chart sets.
// The generated rest of SVG's globals live on a side object an element allocates only when it names one; these
// are common enough that allocating for them would be the regression, so each is a field. `fill` is here too,
// although BCD files it per shape rather than as a global: it paints every one of them.
//
// Values are strings: SVG attributes carry units and keywords ("50%", "1em", "currentColor").
public abstract partial class SVGElement
{
    /// <summary>
    ///     The colour painting the shape's interior. <c>none</c> leaves it unpainted — which is not the
    ///     same as transparent, since an unpainted interior does not receive pointer events either.
    /// </summary>
    public string? Fill { get; set; }

    /// <summary>How opaque the fill is, from 0 to 1.</summary>
    public string? FillOpacity { get; set; }

    /// <summary>
    ///     How to decide what counts as inside a self-intersecting path: <c>nonzero</c> (the default) or
    ///     <c>evenodd</c>.
    /// </summary>
    public string? FillRule { get; set; }

    /// <summary>The colour painting the shape's outline. Unset means no outline is drawn.</summary>
    public string? Stroke { get; set; }

    /// <summary>The outline's thickness in user units.</summary>
    public string? StrokeWidth { get; set; }

    /// <summary>How opaque the outline is, from 0 to 1.</summary>
    public string? StrokeOpacity { get; set; }

    /// <summary>How an open line ends: <c>butt</c>, <c>round</c>, or <c>square</c>.</summary>
    public string? StrokeLinecap { get; set; }

    /// <summary>How two line segments meet: <c>miter</c>, <c>round</c>, or <c>bevel</c>.</summary>
    public string? StrokeLinejoin { get; set; }

    /// <summary>The dash-and-gap pattern for the outline, as a comma- or space-separated list of lengths.</summary>
    public string? StrokeDasharray { get; set; }

    /// <summary>How far into the dash pattern to start. Animating it is the usual way to draw a line on.</summary>
    public string? StrokeDashoffset { get; set; }

    /// <summary>
    ///     How opaque the whole element is, applied after it has been painted — so unlike fill and stroke
    ///     opacity, overlapping parts of one shape do not show through each other.
    /// </summary>
    public string? Opacity { get; set; }

    /// <summary>
    ///     A list of transforms applied to this element and its children: <c>translate()</c>,
    ///     <c>rotate()</c>, <c>scale()</c>, <c>skewX()</c>, <c>matrix()</c>.
    /// </summary>
    public string? Transform { get; set; }

    /// <summary>A reference to a <c>clipPath</c> that clips this element, as <c>url(#id)</c>.</summary>
    public string? ClipPath { get; set; }

    /// <summary>
    ///     The value <c>currentColor</c> resolves to for this element and its children — the usual way to
    ///     let CSS drive an icon's colour.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    ///     Whether the element is rendered. <c>none</c> also removes it from hit-testing and from the
    ///     bounding-box calculation, which <c>Visibility</c> does not.
    /// </summary>
    public string? Display { get; set; }

    /// <summary>Whether the element is visible. <c>hidden</c> still takes up its place in layout and bounding boxes.</summary>
    public string? Visibility { get; set; }

    /// <summary>
    ///     Which parts of the element respond to the pointer — <c>none</c>, <c>visiblePainted</c>,
    ///     <c>all</c>, and so on.
    /// </summary>
    public string? PointerEvents { get; set; }

    // First, where they have always rendered: before the rarer globals and the tag's own attributes.
    partial void WriteOwnedAttributesFirst(StringBuilder sb)
    {
        WritePaintAttributes(sb);
        WritePresentationAttributes(sb);
    }

    private void WritePaintAttributes(StringBuilder sb)
    {
        if (Fill is not null)
        {
            AppendAttr(sb, "fill", Fill);
        }

        if (FillOpacity is not null)
        {
            AppendAttr(sb, "fill-opacity", FillOpacity);
        }

        if (FillRule is not null)
        {
            AppendAttr(sb, "fill-rule", FillRule);
        }

        if (Stroke is not null)
        {
            AppendAttr(sb, "stroke", Stroke);
        }

        if (StrokeWidth is not null)
        {
            AppendAttr(sb, "stroke-width", StrokeWidth);
        }

        if (StrokeOpacity is not null)
        {
            AppendAttr(sb, "stroke-opacity", StrokeOpacity);
        }

        if (StrokeLinecap is not null)
        {
            AppendAttr(sb, "stroke-linecap", StrokeLinecap);
        }

        if (StrokeLinejoin is not null)
        {
            AppendAttr(sb, "stroke-linejoin", StrokeLinejoin);
        }

        if (StrokeDasharray is not null)
        {
            AppendAttr(sb, "stroke-dasharray", StrokeDasharray);
        }

        if (StrokeDashoffset is not null)
        {
            AppendAttr(sb, "stroke-dashoffset", StrokeDashoffset);
        }
    }

    private void WritePresentationAttributes(StringBuilder sb)
    {
        if (Opacity is not null)
        {
            AppendAttr(sb, "opacity", Opacity);
        }

        if (Transform is not null)
        {
            AppendAttr(sb, "transform", Transform);
        }

        if (ClipPath is not null)
        {
            AppendAttr(sb, "clip-path", ClipPath);
        }

        if (Color is not null)
        {
            AppendAttr(sb, "color", Color);
        }

        if (Display is not null)
        {
            AppendAttr(sb, "display", Display);
        }

        if (Visibility is not null)
        {
            AppendAttr(sb, "visibility", Visibility);
        }

        if (PointerEvents is not null)
        {
            AppendAttr(sb, "pointer-events", PointerEvents);
        }
    }
}
