using System.Text;

namespace Rask.Html.Components;

public sealed partial class LinearGradient : SvgElement
{
    protected override string TagName => "linearGradient";

    public string? X1 { get; set; }
    public string? Y1 { get; set; }
    public string? X2 { get; set; }
    public string? Y2 { get; set; }
    public string? GradientUnits { get; set; }
    public string? GradientTransform { get; set; }
    public string? SpreadMethod { get; set; }
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
