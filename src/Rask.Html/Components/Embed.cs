using System.Globalization;
using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     External content handled by a plugin. Legacy: prefer <c>video</c>, <c>audio</c>, <c>img</c>, or
///     <c>iframe</c> for anything new.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/embed">MDN</see>
/// </summary>
public sealed partial class Embed : Element
{
    protected override string TagName => "embed";
    protected override bool SelfClosing => true;

    /// <summary>The URL of the resource to embed.</summary>
    public string? Src { get; set; }

    /// <summary>The resource's MIME type, which selects the handler.</summary>
    public string? Type { get; set; }

    /// <summary>The display width in CSS pixels.</summary>
    public int? Width { get; set; }

    /// <summary>The display height in CSS pixels.</summary>
    public int? Height { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Src is not null)
        {
            AppendUrlAttr(sb, "src", Src);
        }

        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Width is not null)
        {
            AppendAttr(sb, "width", Width.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Height is not null)
        {
            AppendAttr(sb, "height", Height.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
