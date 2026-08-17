using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     An item in an <c>ol</c>, <c>ul</c>, or <c>menu</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/li">MDN</see>
/// </summary>
public sealed class Li : Element
{
    protected override string TagName => "li";

    /// <summary>
    ///     This item's ordinal, which renumbers the items after it. Meaningful only inside an <c>ol</c>.
    /// </summary>
    public int? Value { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
