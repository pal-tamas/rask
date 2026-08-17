using System.Text;

namespace Rask.Html.Components;

public sealed partial class Filter : SvgElement
{
    protected override string TagName => "filter";

    public string? X { get; set; }
    public string? Y { get; set; }
    public string? Width { get; set; }
    public string? Height { get; set; }
    public string? FilterUnits { get; set; }
    public string? PrimitiveUnits { get; set; }

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

        if (FilterUnits is not null)
        {
            AppendAttr(sb, "filterUnits", FilterUnits);
        }

        if (PrimitiveUnits is not null)
        {
            AppendAttr(sb, "primitiveUnits", PrimitiveUnits);
        }
    }
}
