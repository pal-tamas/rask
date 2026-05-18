using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

public sealed class Video : Element
{
    protected override string TagName => "video";

    public string? Src { get; set; }
    public string? Poster { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool Controls { get; set; }
    public bool Autoplay { get; set; }
    public bool Loop { get; set; }
    public bool Muted { get; set; }
    public string? Preload { get; set; }
    public string? CrossOrigin { get; set; }
    public bool PlaysInline { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null) AppendAttr(sb, "src", Src);
        if (Poster is not null) AppendAttr(sb, "poster", Poster);
        if (Width is not null) AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        if (Height is not null) AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        if (Controls) AppendAttr(sb, "controls", null);
        if (Autoplay) AppendAttr(sb, "autoplay", null);
        if (Loop) AppendAttr(sb, "loop", null);
        if (Muted) AppendAttr(sb, "muted", null);
        if (Preload is not null) AppendAttr(sb, "preload", Preload);
        if (CrossOrigin is not null) AppendAttr(sb, "crossorigin", CrossOrigin);
        if (PlaysInline) AppendAttr(sb, "playsinline", null);
    }
}
