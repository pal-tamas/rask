using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A native disclosure widget: collapsed until the user opens it. Its first child should be a
///     <c>summary</c>, which becomes the always-visible label.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/details">MDN</see>
/// </summary>
[Tag("details")]
public sealed partial class Details : Element
{
    /// <summary>
    ///     Whether the disclosure starts expanded. The browser toggles this itself as the user clicks.
    /// </summary>
    public bool? Open { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Open is true)
        {
            AppendAttr(sb, "open", null);
        }
    }
}
