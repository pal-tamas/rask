using System.Text;

namespace Rask.Core.Components;

public sealed class Area : Element
{
    protected override string TagName => "area";
    protected override bool SelfClosing => true;

    public string? Alt { get; set; }
    public string? Coords { get; set; }
    public string? Shape { get; set; }
    public string? Href { get; set; }
    public string? Target { get; set; }
    public string? Rel { get; set; }
    public string? Download { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Alt is not null)
        {
            AppendAttr(sb, "alt", Alt);
        }

        if (Coords is not null)
        {
            AppendAttr(sb, "coords", Coords);
        }

        if (Shape is not null)
        {
            AppendAttr(sb, "shape", Shape);
        }

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
    }
}
