using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     One layer of an <c>feMerge</c>, naming the input to stack.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/feMergeNode">MDN</see>
/// </summary>
public sealed class FeMergeNode : SvgElement
{
    protected override string TagName => "feMergeNode";

    /// <summary>The input to contribute to the merge.</summary>
    public string? In { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (In is not null)
        {
            AppendAttr(sb, "in", In);
        }
    }
}
