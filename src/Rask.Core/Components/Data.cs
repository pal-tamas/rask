using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     Human-readable content paired with a machine-readable equivalent in <c>Value</c>. For dates and
///     times, use <c>time</c> instead.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/data">MDN</see>
/// </summary>
public sealed class Data : Element
{
    protected override string TagName => "data";

    /// <summary>The machine-readable form of the element's text.</summary>
    public string? Value { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }
    }
}
