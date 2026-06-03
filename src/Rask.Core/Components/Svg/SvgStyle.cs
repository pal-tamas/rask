using System.Text;

namespace Rask.Core.Components;

// SVG <style>. Named SvgStyle to avoid colliding with the HTML Style component.
public sealed class SvgStyle : SvgElement
{
    protected override string TagName => "style";

    public string? Type { get; set; }
    public string? Media { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Media is not null)
        {
            AppendAttr(sb, "media", Media);
        }
    }
}
