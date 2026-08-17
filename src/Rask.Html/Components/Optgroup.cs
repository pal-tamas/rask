using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     A named group of <c>option</c> elements inside a <c>select</c>. Groups cannot nest.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/optgroup">MDN</see>
/// </summary>
public sealed partial class Optgroup : Element
{
    protected override string TagName => "optgroup";

    /// <summary>Disables every option in the group.</summary>
    public bool? Disabled { get; set; }

    /// <summary>The group's heading. Required.</summary>
    public new string? Label { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Label is not null)
        {
            AppendAttr(sb, "label", Label);
        }
    }
}
