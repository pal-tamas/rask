using System.Text;

namespace Rask.Html.Components;

public sealed partial class Video : HtmlMediaElement
{
    protected override string TagName => "video";

    public string? Poster { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public bool? PlaysInline { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        // Emits the universal attrs then the shared HtmlMediaElement block (src, controls, …,
        // crossorigin); the video-specific attrs below follow that block.
        base.WriteAttributes(sb);

        if (Poster is not null)
        {
            AppendMediaUrlAttr(sb, "poster", Poster);
        }

        if (Width is { } width)
        {
            AppendAttr(sb, "width", width);
        }

        if (Height is { } height)
        {
            AppendAttr(sb, "height", height);
        }

        if (PlaysInline is true)
        {
            AppendAttr(sb, "playsinline", null);
        }
    }
}
