using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A gradient along a straight line, defined by <c>stop</c> children. Reference it as <c>Fill:
///     "url(#id)"</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/linearGradient">MDN</see>
/// </summary>
public sealed class LinearGradient : SvgElement
{
    protected override string TagName => "linearGradient";

    /// <summary>The gradient vector's start x. Defaults to 0%.</summary>
    public string? X1 { get; set; }

    /// <summary>The gradient vector's start y. Defaults to 0%.</summary>
    public string? Y1 { get; set; }

    /// <summary>The gradient vector's end x. Defaults to 100%.</summary>
    public string? X2 { get; set; }

    /// <summary>The gradient vector's end y. Defaults to 0%, which makes the gradient horizontal.</summary>
    public string? Y2 { get; set; }

    /// <summary>
    ///     The coordinate system for the vector: <c>objectBoundingBox</c> (the default) or
    ///     <c>userSpaceOnUse</c>.
    /// </summary>
    public string? GradientUnits { get; set; }

    /// <summary>An extra transform applied to the gradient.</summary>
    public string? GradientTransform { get; set; }

    /// <summary>
    ///     What happens beyond the vector's ends: <c>pad</c>, <c>reflect</c>, or <c>repeat</c>.
    /// </summary>
    public string? SpreadMethod { get; set; }

    /// <summary>Another gradient to inherit stops and attributes from.</summary>
    public string? Href { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (X1 is not null)
        {
            AppendAttr(sb, "x1", X1);
        }

        if (Y1 is not null)
        {
            AppendAttr(sb, "y1", Y1);
        }

        if (X2 is not null)
        {
            AppendAttr(sb, "x2", X2);
        }

        if (Y2 is not null)
        {
            AppendAttr(sb, "y2", Y2);
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
