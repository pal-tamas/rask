using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Embed : Element
{
    protected override string TagName => "embed";
    protected override bool SelfClosing => true;

    public string? Src { get; set; }
    public string? Type { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null) AppendAttr(sb, "src", Src);
        if (Type is not null) AppendAttr(sb, "type", Type);
        if (Width is not null) AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
    }
}
