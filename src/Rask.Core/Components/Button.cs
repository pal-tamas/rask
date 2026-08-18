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

    /// <summary>Focus this button when the page loads. At most one control per page may ask.</summary>
    public bool? Autofocus { get; set; }

    /// <summary>
    ///     The <c>id</c> of the form this button submits, when the button is not inside it.
    /// </summary>
    public string? Form { get; set; }

    /// <summary>
    ///     Overrides the form's <c>action</c> for this button only — how one form offers "save" and
    ///     "save and publish" as two submit buttons.
    /// </summary>
    public string? FormAction { get; set; }

    /// <summary>Overrides the form's <c>enctype</c> for this button only.</summary>
    public string? FormEnctype { get; set; }

    /// <summary>Overrides the form's <c>method</c> for this button only.</summary>
    public string? FormMethod { get; set; }

    /// <summary>Submits without running the form's validation — a "save draft" button.</summary>
    public bool? FormNovalidate { get; set; }

    /// <summary>Overrides the form's <c>target</c> for this button only.</summary>
    public string? FormTarget { get; set; }

    /// <summary>
    ///     The <c>id</c> of the popover this button controls. The other half of the <c>Popover</c> global
    ///     attribute: the popover declares itself, and this is what opens it without script.
    /// </summary>
    public string? PopoverTarget { get; set; }

    /// <summary><c>toggle</c> (the default), <c>show</c> or <c>hide</c> for <see cref="PopoverTarget" />.</summary>
    public string? PopoverTargetAction { get; set; }

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

        if (Autofocus is true)
        {
            AppendAttr(sb, "autofocus", null);
        }

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

        // Sanitised like every other URL-valued attribute: formaction is a navigation target, so a
        // `javascript:` value here is script execution on submit.
        if (FormAction is not null)
        {
            AppendUrlAttr(sb, "formaction", FormAction);
        }

        if (FormEnctype is not null)
        {
            AppendAttr(sb, "formenctype", FormEnctype);
        }

        if (FormMethod is not null)
        {
            AppendAttr(sb, "formmethod", FormMethod);
        }

        if (FormNovalidate is true)
        {
            AppendAttr(sb, "formnovalidate", null);
        }

        if (FormTarget is not null)
        {
            AppendAttr(sb, "formtarget", FormTarget);
        }

        if (PopoverTarget is not null)
        {
            AppendAttr(sb, "popovertarget", PopoverTarget);
        }

        if (PopoverTargetAction is not null)
        {
            AppendAttr(sb, "popovertargetaction", PopoverTargetAction);
        }
    }
}
