using System.Text;

namespace Rask.Core.Components;

public sealed class Pattern : SvgElement
{
    protected override string TagName => "pattern";

    public string? X { get; set; }
    public string? Y { get; set; }
    public string? Width { get; set; }
    public string? Height { get; set; }
    public string? PatternUnits { get; set; }
    public string? PatternContentUnits { get; set; }
    public string? PatternTransform { get; set; }
    public string? ViewBox { get; set; }
    public string? PreserveAspectRatio { get; set; }
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
