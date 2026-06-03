using System.Text;

namespace Rask.Core.Components;

public sealed class FeDropShadow : SvgElement
{
    protected override string TagName => "feDropShadow";

    public string? In { get; set; }
    public string? Dx { get; set; }
    public string? Dy { get; set; }
    public string? StdDeviation { get; set; }
    public string? FloodColor { get; set; }
    public string? FloodOpacity { get; set; }
    public string? Result { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }

        if (Dx is not null)
        {
            AppendAttr(sb, "dx", Dx);
        }

        if (Dy is not null)
        {
            AppendAttr(sb, "dy", Dy);
        }

        if (StdDeviation is not null)
        {
            AppendAttr(sb, "stdDeviation", StdDeviation);
        }

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
