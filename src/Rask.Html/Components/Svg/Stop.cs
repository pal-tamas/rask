using System.Text;

namespace Rask.Html.Components;

public sealed partial class Stop : SvgElement
{
    protected override string TagName => "stop";

    public string? Offset { get; set; }
    public string? StopColor { get; set; }
    public string? StopOpacity { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Offset is not null)
        {
            AppendAttr(sb, "offset", Offset);
        }

        if (StopColor is not null)
        {
            AppendAttr(sb, "stop-color", StopColor);
        }

        if (StopOpacity is not null)
        {
            AppendAttr(sb, "stop-opacity", StopOpacity);
        }
    }
}
