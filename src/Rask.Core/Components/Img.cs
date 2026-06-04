using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Img : Element
{
    protected override string TagName => "img";
    protected override bool SelfClosing => true;

    public string? Src { get; set; }
    public string? Alt { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Loading { get; set; }
    public string? Srcset { get; set; }
    public string? Sizes { get; set; }
    public string? CrossOrigin { get; set; }
    public string? ReferrerPolicy { get; set; }
    public string? Decoding { get; set; }
    public string? UseMap { get; set; }
    public bool? Ismap { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendAttr(sb, "src", Src);
        }

        if (Alt is not null)
        {
            AppendAttr(sb, "alt", Alt);
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

        if (Srcset is not null)
        {
            AppendAttr(sb, "srcset", Srcset);
        }

        if (Sizes is not null)
        {
            AppendAttr(sb, "sizes", Sizes);
        }

        if (CrossOrigin is not null)
        {
            AppendAttr(sb, "crossorigin", CrossOrigin);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }

        if (Decoding is not null)
        {
            AppendAttr(sb, "decoding", Decoding);
        }

        if (UseMap is not null)
        {
            AppendAttr(sb, "usemap", UseMap);
        }

        if (Ismap is true)
        {
            AppendAttr(sb, "ismap", null);
        }
    }
}
