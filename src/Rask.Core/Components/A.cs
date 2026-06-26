using System.Text;

namespace Rask.Core.Components;

public sealed class A : Element
{
    protected override string TagName => "a";

    public string? Href { get; set; }
    public string? Target { get; set; }
    public string? Rel { get; set; }
    public string? Download { get; set; }
    public string? Hreflang { get; set; }
    public string? Type { get; set; }
    public string? ReferrerPolicy { get; set; }
    public string? Ping { get; set; }

    // OnClick / OnClickAsync are inherited from Element (the GlobalEventHandlers surface) — no longer
    // declared per-tag. The base emits data-rask-on-click in the universal handler group.

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Href is not null)
        {
            AppendUrlAttr(sb, "href", Href);
        }

        if (Target is not null)
        {
            AppendAttr(sb, "target", Target);
        }

        if (Rel is not null)
        {
            AppendAttr(sb, "rel", Rel);
        }

        if (Download is not null)
        {
            AppendAttr(sb, "download", Download);
        }

        if (Hreflang is not null)
        {
            AppendAttr(sb, "hreflang", Hreflang);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (ReferrerPolicy is not null)
        {
            AppendAttr(sb, "referrerpolicy", ReferrerPolicy);
        }

        if (Ping is not null)
        {
            AppendAttr(sb, "ping", Ping);
        }
    }
}
