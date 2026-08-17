using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A gradient radiating from a focal point out to a circle, defined by <c>stop</c> children.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/radialGradient">MDN</see>
/// </summary>
public sealed class RadialGradient : SvgElement
{
    protected override string TagName => "radialGradient";

    /// <summary>The end circle's centre x. Defaults to 50%.</summary>
    public string? Cx { get; set; }

    /// <summary>The end circle's centre y. Defaults to 50%.</summary>
    public string? Cy { get; set; }

    /// <summary>The end circle's radius. Zero paints the last stop's colour flat.</summary>
    public string? R { get; set; }

    /// <summary>The focal point's x, which offsets the highlight. Defaults to <c>Cx</c>.</summary>
    public string? Fx { get; set; }

    /// <summary>The focal point's y. Defaults to <c>Cy</c>.</summary>
    public string? Fy { get; set; }

    /// <summary>The focal circle's radius.</summary>
    public string? Fr { get; set; }

    /// <summary>
    ///     The coordinate system for the circles: <c>objectBoundingBox</c> or <c>userSpaceOnUse</c>.
    /// </summary>
    public string? GradientUnits { get; set; }

    /// <summary>An extra transform applied to the gradient.</summary>
    public string? GradientTransform { get; set; }

    /// <summary>What happens beyond the end circle: <c>pad</c>, <c>reflect</c>, or <c>repeat</c>.</summary>
    public string? SpreadMethod { get; set; }

    /// <summary>Another gradient to inherit stops and attributes from.</summary>
    public string? Href { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Cx is not null)
        {
            AppendAttr(sb, "cx", Cx);
        }

        if (Cy is not null)
        {
            AppendAttr(sb, "cy", Cy);
        }

        if (R is not null)
        {
            AppendAttr(sb, "r", R);
        }

        if (Fx is not null)
        {
            AppendAttr(sb, "fx", Fx);
        }

        if (Fy is not null)
        {
            AppendAttr(sb, "fy", Fy);
        }

        if (Fr is not null)
        {
            AppendAttr(sb, "fr", Fr);
        }

        if (GradientUnits is not null)
        {
            AppendAttr(sb, "gradientUnits", GradientUnits);
        }

        if (GradientTransform is not null)
        {
            AppendAttr(sb, "gradientTransform", GradientTransform);
        }

        if (SpreadMethod is not null)
        {
            AppendAttr(sb, "spreadMethod", SpreadMethod);
        }

        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }
    }
}
