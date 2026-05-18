using System.Text;

namespace Rask.Core.Components;

public sealed class Link : Element
{
    protected override string TagName => "link";
    protected override bool SelfClosing => true;

    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Type { get; set; }
    public string? Media { get; set; }
    public string? Sizes { get; set; }
    public string? Hreflang { get; set; }
    public string? As { get; set; }
    public string? CrossOrigin { get; set; }
    public string? ReferrerPolicy { get; set; }
    public bool Disabled { get; set; }
    public string? Color { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null) AppendAttr(sb, "href", Href);
        if (Rel is not null) AppendAttr(sb, "rel", Rel);
        if (Type is not null) AppendAttr(sb, "type", Type);
        if (Media is not null) AppendAttr(sb, "media", Media);
        if (Sizes is not null) AppendAttr(sb, "sizes", Sizes);
        if (Hreflang is not null) AppendAttr(sb, "hreflang", Hreflang);
        if (As is not null) AppendAttr(sb, "as", As);
        if (CrossOrigin is not null) AppendAttr(sb, "crossorigin", CrossOrigin);
        if (ReferrerPolicy is not null) AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        if (Disabled) AppendAttr(sb, "disabled", null);
        if (Color is not null) AppendAttr(sb, "color", Color);
    }
}
