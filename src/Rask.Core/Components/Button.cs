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

    /// <summary>
    ///     The <c>id</c> of the form this button belongs to, when it is not nested inside one.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/button#form">MDN</see>
    /// </summary>
    public string? Form { get; set; }

    // The form-override set. `Input` has had all six since it was written, so until now a submit button
    // could override the form's action spelled as <input type="submit"> but not as <button> — an
    // inconsistency rather than a decision (#694). They apply only to a submit button.

    /// <summary>Overrides the form's <c>action</c> when this button submits it.</summary>
    public string? FormAction { get; set; }

    /// <summary>Overrides the form's <c>enctype</c> when this button submits it.</summary>
    public string? FormEnctype { get; set; }

    /// <summary>Overrides the form's <c>method</c> when this button submits it.</summary>
    public string? FormMethod { get; set; }

    /// <summary>Skips validation when this button submits the form — a "save draft" button.</summary>
    public bool? FormNovalidate { get; set; }

    /// <summary>Overrides the form's <c>target</c> when this button submits it.</summary>
    public string? FormTarget { get; set; }

    /// <summary>
    ///     The <c>id</c> of a popover this button controls — the other half of <c>Element.Popover</c>.
    ///     The browser handles opening, light-dismiss, the top layer and focus, with no JavaScript.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/button#popovertarget">MDN</see>
    /// </summary>
    public string? PopoverTarget { get; set; }

    /// <summary>
    ///     What <c>PopoverTarget</c> does: <c>"toggle"</c> (the default), <c>"show"</c> or <c>"hide"</c>.
    /// </summary>
    public string? PopoverTargetAction { get; set; }

    /// <summary>
    ///     Focuses this button on page load. At most one control per page may set it, and moving focus on
    ///     load disorients screen-reader and magnifier users — so reserve it for a page whose entire
    ///     purpose is the control (a search page), never a general form.
    ///     <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/autofocus">MDN</see>
    /// </summary>
    public bool? Autofocus { get; set; }

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

        if (Form is not null)
        {
            AppendAttr(sb, "form", Form);
        }

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

        if (Autofocus is true)
        {
            AppendAttr(sb, "autofocus", null);
        }
    }
}
