using System.Text;

namespace Rask.Html.Components;

// Base for SVG element tags (svg, g, path, circle, …). Mirrors Element: it is abstract so the
// factory generator emits no factory for it, but its public properties are inherited by every
// concrete SVG tag and therefore surface as optional factory parameters (same mechanism that
// flows Id/Class/Style/Data down from Element).
//
// It carries the common SVG *presentation* attributes — the ones that apply across virtually all
// SVG elements — plus a universal click handler so any shape is interactive through the normal
// data-rask-on-click event-delegation path. Tag-specific geometry attributes (cx, d, points, …)
// live on the concrete subclasses.
//
// Values are string? throughout: SVG attributes routinely carry units or keywords ("50%", "1em",
// "currentColor") that a numeric type couldn't express. PascalCase property names map to the real
// hyphenated/camelCase SVG attribute names explicitly in WriteAttributes.

/// <summary>
///     The presentation attributes every SVG shape shares — fill, stroke, opacity and transform. Not a tag
///     of its own: it exists so each shape exposes the same painting surface as optional factory
///     parameters. Every one of these is also a CSS property, so a stylesheet can set them instead. <see
///     href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Attribute#presentation_attributes">MDN:
///     presentation attributes</see>
/// </summary>
public abstract partial class SvgElement : Element
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

    /// <summary>
    ///     The dash-and-gap pattern for the outline, as a comma- or space-separated list of lengths.
    /// </summary>
    public string? StrokeDasharray { get; set; }

    /// <summary>
    ///     How far into the dash pattern to start. Animating it is the usual way to draw a line on.
    /// </summary>
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
    public new string? ClipPath { get; set; }

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

    /// <summary>
    ///     Whether the element is visible. <c>hidden</c> still takes up its place in layout and bounding
    ///     boxes.
    /// </summary>
    public string? Visibility { get; set; }

    /// <summary>
    ///     Which parts of the element respond to the pointer — <c>none</c>, <c>visiblePainted</c>,
    ///     <c>all</c>, and so on.
    /// </summary>
    public string? PointerEvents { get; set; }

    // OnClick (and the rest of the GlobalEventHandlers surface) is inherited from Element — any SVG
    // shape is interactive through the universal data-rask-on-* path without redeclaring it here.

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

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
