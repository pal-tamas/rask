using System.Text;

namespace Rask.Html.Components;

/// <summary>
///     One choice in a <c>select</c>, <c>optgroup</c>, or <c>datalist</c>.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/option">MDN</see>
/// </summary>
public sealed partial class Option : Element
{
    protected override string TagName => "option";

    /// <summary>The value submitted when this option is chosen. Defaults to the option's text.</summary>
    public string? Value { get; set; }

    /// <summary>Whether this option starts selected.</summary>
    public bool? Selected { get; set; }

    /// <summary>Makes the option unselectable.</summary>
    public bool? Disabled { get; set; }

    /// <summary>A shorter label to display instead of the option's text.</summary>
    public new string? Label { get; set; }

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }

        if (Selected is true)
        {
            AppendAttr(sb, "selected", null);
        }

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
