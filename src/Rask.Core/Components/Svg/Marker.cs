using System.Text;

namespace Rask.Core.Components;

public sealed class Marker : SvgElement
{
    protected override string TagName => "marker";

    public string? MarkerWidth { get; set; }
    public string? MarkerHeight { get; set; }
    public string? RefX { get; set; }
    public string? RefY { get; set; }
    public string? Orient { get; set; }
    public string? MarkerUnits { get; set; }
    public string? ViewBox { get; set; }
    public string? PreserveAspectRatio { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (MarkerWidth is not null)
        {
            AppendAttr(sb, "markerWidth", MarkerWidth);
        }

        if (MarkerHeight is not null)
        {
            AppendAttr(sb, "markerHeight", MarkerHeight);
        }

        if (RefX is not null)
        {
            AppendAttr(sb, "refX", RefX);
        }

        if (RefY is not null)
        {
            AppendAttr(sb, "refY", RefY);
        }

        if (Orient is not null)
        {
            AppendAttr(sb, "orient", Orient);
        }

        if (MarkerUnits is not null)
        {
            AppendAttr(sb, "markerUnits", MarkerUnits);
        }

        if (ViewBox is not null)
        {
            AppendAttr(sb, "viewBox", ViewBox);
        }

        if (PreserveAspectRatio is not null)
        {
            AppendAttr(sb, "preserveAspectRatio", PreserveAspectRatio);
        }
    }
}
