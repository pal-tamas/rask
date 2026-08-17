using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A placeholder in a shadow tree filled by the light DOM. A web-components primitive — Rask's own
///     composition is the <c>[...]</c> children indexer.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/slot">MDN</see>
/// </summary>
public sealed partial class Slot : Element
{
    protected override string TagName => "slot";

    /// <summary>
    ///     The slot's name, matched by a child's <c>slot</c> attribute. Unnamed, it is the default slot.
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
