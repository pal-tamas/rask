using System.Text;

namespace Rask.Core.Components;

public sealed class FeFlood : SvgElement
{
    protected override string TagName => "feFlood";

    public string? FloodColor { get; set; }
    public string? FloodOpacity { get; set; }
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (FloodColor is not null)
        {
            AppendAttr(sb, "flood-color", FloodColor);
        }

        if (FloodOpacity is not null)
        {
            AppendAttr(sb, "flood-opacity", FloodOpacity);
        }

        if (Result is not null)
        {
            AppendAttr(sb, "result", Result);
        }
    }
}
