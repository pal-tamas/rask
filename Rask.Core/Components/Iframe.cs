using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Iframe : Element
{
    protected override string TagName => "iframe";

    public string? Src { get; set; }
    public string? Srcdoc { get; set; }
    public string? Name { get; set; }
    public string? Sandbox { get; set; }
    public string? Allow { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Loading { get; set; }
    public string? ReferrerPolicy { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendAttr(sb, "src", Src);
        }

        if (Srcdoc is not null)
        {
            AppendAttr(sb, "srcdoc", Srcdoc);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Sandbox is not null)
        {
            AppendAttr(sb, "sandbox", Sandbox);
        }

        if (Allow is not null)
        {
            AppendAttr(sb, "allow", Allow);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Loading is not null)
        {
            AppendAttr(sb, "loading", Loading);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }
    }
}
