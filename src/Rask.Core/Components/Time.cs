using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     A date or time in a machine-readable form. Put the precise value in <c>DateTime</c> and the human
///     wording in the text.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/time">MDN</see>
/// </summary>
public sealed class Time : Element
{
    protected override string TagName => "time";

    /// <summary>
    ///     The machine-readable instant or duration, in one of the formats HTML defines —
    ///     <c>2026-08-14</c>, <c>14:30</c>, <c>2026-08-14T14:30Z</c>, <c>PT2H30M</c>.
    /// </summary>
    public string? DateTime { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (DateTime is not null)
        {
            AppendAttr(sb, "datetime", DateTime);
        }
    }
}
