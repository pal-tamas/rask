using System.Text;

namespace Rask.Core.Components;

/// <summary>
///     An interactive button. Always set <c>Type</c>: inside a form an unset type means <c>submit</c>,
///     which is the usual cause of a page that reloads when you did not expect it.
///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/button">MDN</see>
/// </summary>
public sealed class Button : Element
{
    protected override string TagName => "button";

    /// <summary>
    ///     <c>submit</c> (the default inside a form), <c>reset</c>, or <c>button</c> for a button that does
    ///     nothing on its own.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    ///     Makes the button unclickable and skips it in form submission. A disabled control is not
    ///     focusable, so it cannot announce why it is disabled.
    /// </summary>
    public bool? Disabled { get; set; }

    /// <summary>The name submitted with the form when this button is the one that submitted it.</summary>
    public string? Name { get; set; }

    /// <summary>The value submitted alongside <c>Name</c>.</summary>
    public string? Value { get; set; }

    // OnClick / OnClickAsync are inherited from Element (the GlobalEventHandlers surface).

    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);
        if (Type is not null)
        {
            AppendAttr(sb, "type", Type);
        }

        if (Disabled is true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Name is not null)
        {
            AppendAttr(sb, "name", Name);
        }

        if (Value is not null)
        {
            AppendAttr(sb, "value", Value);
        }
    }
}
