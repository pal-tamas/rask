using System.Text;

namespace Rask.Html.Components;

public sealed partial class Mask : SvgElement
{
    protected override string TagName => "mask";

    public string? MaskUnits { get; set; }
    public string? MaskContentUnits { get; set; }
    public string? X { get; set; }
    public string? Y { get; set; }
    public string? Width { get; set; }
    public string? Height { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (MaskUnits is not null)
        {
            AppendAttr(sb, "maskUnits", MaskUnits);
        }

        if (MaskContentUnits is not null)
        {
            AppendAttr(sb, "maskContentUnits", MaskContentUnits);
        }

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
    }
}
