using System.Text;

namespace Rask.Core.Components;

public sealed class Audio : Element
{
    protected override string TagName => "audio";

    public string? Src { get; set; }
    public bool Controls { get; set; }
    public bool Autoplay { get; set; }
    public bool Loop { get; set; }
    public bool Muted { get; set; }
    public string? Preload { get; set; }
    public string? CrossOrigin { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendAttr(sb, "src", Src);
        }

        if (Controls)
        {
            AppendAttr(sb, "controls", null);
        }

        if (Autoplay)
        {
            AppendAttr(sb, "autoplay", null);
        }

        if (Loop)
        {
            AppendAttr(sb, "loop", null);
        }

        if (Muted)
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
