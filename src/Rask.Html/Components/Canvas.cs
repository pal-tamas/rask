using System.Globalization;
using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A bitmap drawing surface scripted through the Canvas API. Set <c>Width</c>/<c>Height</c> as
///     attributes — they are the drawing buffer's real size, which CSS dimensions only scale.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/canvas">MDN</see>
/// </summary>
public sealed partial class Canvas : Element
{
    protected override string TagName => "canvas";

    /// <summary>
    ///     The drawing buffer's width in pixels (default 300). Distinct from the CSS width, which merely
    ///     scales the result.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    ///     The drawing buffer's height in pixels (default 150). Distinct from the CSS height, which merely
    ///     scales the result.
    /// </summary>
    public int? Height { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
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
