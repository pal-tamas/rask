using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     An image map: a set of <c>area</c> children an <c>img</c> references through its <c>UseMap</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/map">MDN</see>
/// </summary>
public sealed partial class Map : Element
{
    protected override string TagName => "map";

    /// <summary>
    ///     The map's name, which an image references as <c>UseMap: "#name"</c>. Required, and must be
    ///     unique.
    /// </summary>
    public string? Name { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }
    }
}
