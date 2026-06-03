using System.Text;

namespace Rask.Core.Components;

public sealed class TextPath : SvgElement
{
    protected override string TagName => "textPath";

    public string? Href { get; set; }
    public string? StartOffset { get; set; }
    public string? Method { get; set; }
    public string? Spacing { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendAttr(sb, "href", Href);
        }

        if (StartOffset is not null)
        {
            AppendAttr(sb, "startOffset", StartOffset);
        }

        if (Method is not null)
        {
            AppendAttr(sb, "method", Method);
        }

        if (Spacing is not null)
        {
            AppendAttr(sb, "spacing", Spacing);
        }
    }
}
