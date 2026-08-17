using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Overrides the current text direction for its children, rendering them in the direction <c>Dir</c>
///     names.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/bdo">MDN</see>
/// </summary>
public sealed class Bdo : Element
{
    protected override string TagName => "bdo";

    /// <summary>
    ///     The direction to impose: <c>ltr</c> or <c>rtl</c>. Required — the element does nothing without
    ///     it.
    /// </summary>
    public string? Dir { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Dir is not null)
        {
            AppendAttr(sb, "dir", Dir);
        }
    }
}
