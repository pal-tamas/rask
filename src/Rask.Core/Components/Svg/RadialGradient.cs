using System.Text;

namespace Rask.Core.Components;

public sealed class RadialGradient : SvgElement
{
    protected override string TagName => "radialGradient";

    public string? Cx { get; set; }
    public string? Cy { get; set; }
    public string? R { get; set; }
    public string? Fx { get; set; }
    public string? Fy { get; set; }
    public string? Fr { get; set; }
    public string? GradientUnits { get; set; }
    public string? GradientTransform { get; set; }
    public string? SpreadMethod { get; set; }
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
