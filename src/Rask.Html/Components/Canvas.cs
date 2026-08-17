using System.Globalization;
using System.Text;

namespace Rask.Html.Components;

public sealed partial class Canvas : Element
{
    protected override string TagName => "canvas";

    public int? Width { get; set; }
    public int? Height { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
