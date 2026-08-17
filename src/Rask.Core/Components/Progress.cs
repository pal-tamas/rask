using System.Globalization;
using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     How far along a task is. Leave <c>Value</c> unset for an indeterminate bar — the spinner state for
///     work whose length is unknown.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/progress">MDN</see>
/// </summary>
public sealed class Progress : Element
{
    protected override string TagName => "progress";

    /// <summary>How much of the task is done. Omit it entirely for an indeterminate bar.</summary>
    public double? Value { get; set; }

    /// <summary>The value that counts as complete (default 1).</summary>
    public double? Max { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (Max is not null)
        {
            AppendAttr(sb, "max", Max.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
