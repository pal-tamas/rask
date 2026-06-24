using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Components;

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
public abstract class SvgElement : Element
{
    public string? Fill { get; set; }
    public string? FillOpacity { get; set; }
    public string? FillRule { get; set; }
    public string? Stroke { get; set; }
    public string? StrokeWidth { get; set; }
    public string? StrokeOpacity { get; set; }
    public string? StrokeLinecap { get; set; }
    public string? StrokeLinejoin { get; set; }
    public string? StrokeDasharray { get; set; }
    public string? StrokeDashoffset { get; set; }
    public string? Opacity { get; set; }
    public string? Transform { get; set; }
    public string? ClipPath { get; set; }
    public string? Color { get; set; }
    public string? Display { get; set; }
    public string? Visibility { get; set; }
    public string? PointerEvents { get; set; }

    public Callback? OnClick { get; set; }
    public CallbackAsync? OnClickAsync { get; set; }

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

        var click = (Delegate?)OnClick ?? OnClickAsync;
        if (click is not null && LiveRenderContext.CurrentSync is { } ctx)
        {
            AppendAttr(sb, "data-rask-on-click", ctx.RegisterHandler(click));
        }
    }
}
