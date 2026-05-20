using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

// Renders the <object> HTML tag. Renamed from Object to avoid shadowing System.Object.
public sealed class HtmlObject : Element
{
    protected override string TagName => "object";

    public string? DataUrl { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Form { get; set; }
    public string? UseMap { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (DataUrl is not null)
        {
            AppendAttr(sb, "data", DataUrl);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        if (UseMap is not null)
        {
            AppendAttr(sb, "usemap", UseMap);
        }
    }
}
