using System.Text;

namespace Rask.Html.Components;

public sealed partial class Svg : SvgElement
{
    protected override string TagName => "svg";

    public string? Width { get; set; }
    public string? Height { get; set; }
    public string? ViewBox { get; set; }
    public string? PreserveAspectRatio { get; set; }
    public string? X { get; set; }
    public string? Y { get; set; }
    public string? Xmlns { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Width is not null)
        {
            AppendAttr(sb, "width", Width);
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height);
        }

        if (ViewBox is not null)
        {
            AppendAttr(sb, "viewBox", ViewBox);
        }

        if (PreserveAspectRatio is not null)
        {
            AppendAttr(sb, "preserveAspectRatio", PreserveAspectRatio);
        }

        if (X is not null)
        {
            AppendAttr(sb, "x", X);
        }

        if (Y is not null)
        {
            AppendAttr(sb, "y", Y);
        }

        if (Xmlns is not null)
        {
            AppendAttr(sb, "xmlns", Xmlns);
        }
    }
}
