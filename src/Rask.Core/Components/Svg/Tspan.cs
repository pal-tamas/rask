using System.Text;

namespace Rask.Core.Components;

public sealed class Tspan : SvgElement
{
    protected override string TagName => "tspan";

    public string? X { get; set; }
    public string? Y { get; set; }
    public string? Dx { get; set; }
    public string? Dy { get; set; }
    public string? Rotate { get; set; }
    public string? TextAnchor { get; set; }
    public string? LengthAdjust { get; set; }
    public string? TextLength { get; set; }

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

        if (Dx is not null)
        {
            AppendAttr(sb, "dx", Dx);
        }

        if (Dy is not null)
        {
            AppendAttr(sb, "dy", Dy);
        }

        if (Rotate is not null)
        {
            AppendAttr(sb, "rotate", Rotate);
        }

        if (TextAnchor is not null)
        {
            AppendAttr(sb, "text-anchor", TextAnchor);
        }

        if (LengthAdjust is not null)
        {
            AppendAttr(sb, "lengthAdjust", LengthAdjust);
        }

        if (TextLength is not null)
        {
            AppendAttr(sb, "textLength", TextLength);
        }
    }
}
