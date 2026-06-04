using System.Text;

namespace Rask.Core.Components;

public sealed class Audio : Element
{
    protected override string TagName => "audio";

    public string? Src { get; set; }
    public bool? Controls { get; set; }
    public bool? Autoplay { get; set; }
    public bool? Loop { get; set; }
    public bool? Muted { get; set; }
    public string? Preload { get; set; }
    public string? CrossOrigin { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendAttr(sb, "src", Src);
        }

        if (Controls is true)
        {
            AppendAttr(sb, "controls", null);
        }

        if (Autoplay is true)
        {
            AppendAttr(sb, "autoplay", null);
        }

        if (Loop is true)
        {
            AppendAttr(sb, "loop", null);
        }

        if (Muted is true)
        {
            AppendAttr(sb, "muted", null);
        }

        if (Preload is not null)
        {
            AppendAttr(sb, "preload", Preload);
        }

        if (CrossOrigin is not null)
        {
            AppendAttr(sb, "crossorigin", CrossOrigin);
        }
    }
}
