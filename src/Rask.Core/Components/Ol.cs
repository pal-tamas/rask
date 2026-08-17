using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     An ordered list, where the sequence carries meaning. Reordering the items changes what it says.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/ol">MDN</see>
/// </summary>
public sealed class Ol : Element
{
    protected override string TagName => "ol";

    /// <summary>
    ///     The numbering style: <c>1</c>, <c>a</c>, <c>A</c>, <c>i</c>, or <c>I</c>. Unlike the CSS
    ///     equivalent, this one is copied when the reader copies the text.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>Numbers the list in descending order.</summary>
    public bool? Reversed { get; set; }

    /// <summary>The ordinal the list starts at, always given as a number whatever <c>Type</c> is.</summary>
    public int? Start { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Reversed is true)
        {
            AppendAttr(sb, "reversed", null);
        }

        if (Start is not null)
        {
            AppendAttr(sb, "start", Start.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
