using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// What every form control in the kit has in common: an optional visible label, an optional hint, the
/// validation message, and the three axes.
/// </summary>
/// <remarks>
/// <para>
/// A field is three parts — a label, a control, and the message that appears when the value is rejected —
/// and before this each control either declared them itself or left them to the call site. The showcase
/// wrote the first and third by hand ~86 times as
/// <c>Label.Class(Tw.Label)["Username"], Input.Bind(…).Class(Tw.Input)</c>, once per field, which is how
/// a label ends up associated with nothing in particular.
/// </para>
/// <para>
/// THE ASSOCIATION IS THE REASON THIS EXISTS, not the duplication. A label reaches its control either by
/// <c>for</c>/<c>id</c> or by wrapping it, and both are easy to get wrong in a way nothing reports: the
/// text renders, the control renders, clicking the text does nothing and a screen reader announces an
/// unnamed field. Doing it once is what makes it right everywhere instead of right where someone
/// remembered.
/// </para>
/// <para>
/// BY <c>for</c>/<c>id</c>, NOT BY WRAPPING, and that was learnt the hard way. Wrapping is tidier — the
/// nesting is the association, so there is no id to mint or keep unique — and it breaks daisyUI. Its
/// validator message is revealed by a GENERAL SIBLING selector,
/// <c>.validator:user-invalid ~ .validator-hint</c>, so a control moved inside a label is no longer a
/// sibling of its own message: the message rendered, carried the right text, and stayed
/// <c>visibility: hidden</c> for the life of the page. It breaks a <see cref="UiValidator" /> the call
/// site places itself the same way, which is an existing pairing this had no business changing. A browser
/// test caught it; nothing that reads markup could have.
/// </para>
/// <para>
/// So the id matters, and <see cref="Id" /> is optional — when it is not given one is DERIVED: from the
/// bound member's name, or failing that from the label text. Deterministic on purpose, so the markup is
/// reproducible across renders and in the golden files, rather than a fresh GUID per instance.
/// </para>
/// <para>
/// <see cref="Label" /> is OPTIONAL. A search box whose placeholder is its whole affordance, a control
/// inside a table cell, a field whose label is a column header — all real, and all render just the
/// control. When there is no visible label, name it with <see cref="AccessibleLabel" />: a placeholder is
/// not a name, because it disappears the moment someone types.
/// </para>
/// <para>
/// Derived controls implement <see cref="Control" /> and nothing else about the field's shape. That
/// division is deliberate: a checkbox's label belongs AFTER its box and a file input has no placeholder,
/// so a base that laid out the whole field would be wrong for the controls that differ most. This one
/// owns the wrapper, the label, the hint and the message; the control owns its own markup.
/// </para>
/// </remarks>
public abstract partial class UiFormField<T> : Component, IFormControl<T>
{
    /// <summary>The visible label. Omit it for a control that is named some other way.</summary>
    public string? Label { get; set; }

    /// <summary>
    ///     The accessible name, for a control with no visible <see cref="Label" />.
    /// </summary>
    /// <remarks>
    ///     Rendered as <c>aria-label</c>, and ignored when <see cref="Label" /> is set — two names on one
    ///     control is worse than one, because the one a screen reader reads is then not the one on screen.
    /// </remarks>
    public string? AccessibleLabel { get; set; }

    /// <summary>
    ///     Whether to render the field's validation message under the control. On by default for a BOUND
    ///     field.
    /// </summary>
    /// <remarks>
    ///     Turn it off for a field whose errors are shown somewhere else — a summary at the top of a form,
    ///     or one message for a group of fields — so the same error is not said twice.
    /// </remarks>
    public bool? ShowValidation { get; set; }

    /// <summary>
    ///     The message to show when what was typed is not acceptable, for a CONTROLLED field.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A bound field needs none of this: it names a model property, so it looks its own messages up and
    ///     <see cref="ShowValidation" /> is all there is to decide. A controlled field's parent owns the
    ///     value, so it owns the verdict too — and until this existed the only way to show it was to place a
    ///     <see cref="UiValidator" /> as a SIBLING of the control and rely on daisyUI's
    ///     <c>.validator ~ .validator-hint</c> reaching it.
    ///     </para>
    ///     <para>
    ///     That adjacency is exactly the fragility this type exists to remove. It survived as long as a
    ///     field was a bare control; the moment one grew a label and a wrapper, the message was no longer a
    ///     sibling and silently stopped appearing. Given here it is rendered next to the control, inside the
    ///     wrapper, so the relationship the CSS needs is the component's business rather than the call
    ///     site's.
    ///     </para>
    ///     <para>
    ///     Pair it with <c>Tone(UiTone.Error)</c>: the tone is what marks the control invalid, and the
    ///     invalid state is what reveals the message.
    ///     </para>
    /// </remarks>
    public string? Error { get; set; }

    /// <summary>Help text under the control — a format, a constraint, why it is being asked for.</summary>
    /// <remarks>
    ///     Outside the label rather than inside it: a label's text becomes the control's accessible name,
    ///     and a name that recites the hint every time is worse for a screen reader than one that does not.
    /// </remarks>
    public string? Hint { get; set; }

    /// <summary>
    ///     The control's colour. <see cref="UiTone.Error" /> also marks it invalid to assistive tech.
    /// </summary>
    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public T? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<T>? OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<T>? AfterBind { get; set; }

    /// <summary>The control itself — an <c>&lt;input&gt;</c>, a <c>&lt;select&gt;</c>, a textarea.</summary>
    protected abstract Component Control();

    /// <summary>
    ///     <c>aria-*</c> for the control, with the accessible name and the invalid state resolved.
    /// </summary>
    /// <remarks>
    ///     <c>aria-invalid</c> is what makes daisyUI reveal a following validator message, and what a
    ///     screen reader needs — a field that is visibly red and says nothing is half a message. It is
    ///     OMITTED rather than set to null: a null renders the attribute valueless, and a valueless
    ///     <c>aria-invalid</c> reads as "true", which would mark every field in the kit invalid.
    /// </remarks>
    protected Dictionary<string, string?> ControlAria()
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);

        // Only when there is no visible label — the wrapper's <label> already names it, and an aria-label
        // would override the visible text with a second answer.
        if (Label is null && AccessibleLabel is { } name)
        {
            aria["label"] = name;
        }

        if (Tone == UiTone.Error)
        {
            aria["invalid"] = "true";
        }

        return aria;
    }

    /// <summary>
    ///     The field's own validation message, or null when there is nothing to show one for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Only for a BOUND field: a message needs a field name to look itself up by, and that name comes
    ///     from the bind expression. A controlled field's parent owns its value, so it owns its errors too.
    ///     </para>
    ///     <para>
    ///     <c>ValidationMessage</c> renders nothing until the field actually has messages, so this costs an
    ///     empty component rather than an empty box. The showcase wrote this same three-line template once
    ///     per control — and the three copies had drifted to two different colours.
    ///     </para>
    /// </remarks>
    protected Component? ValidationFor() =>
        ShowValidation == false || Bind is not { } bind
            ? null
            : ValidationMessage
                .Template(messages => P.Class("label text-ui-danger-ink")[messages[0]])
                .For(bind);

    /// <summary>
    ///     The id the label points at — the caller's, or one derived from what the field is.
    /// </summary>
    /// <remarks>
    ///     Derived rather than minted so it is the SAME across renders: a fresh GUID per instance would
    ///     change the markup on every pass, which breaks diffing and makes the golden files unreproducible.
    ///     The bound member's name is the best source (it is what the field IS), the label text the
    ///     fallback. Two fields bound to the same member on one page would collide, which is what
    ///     <see cref="Id" /> is for.
    /// </remarks>
    /// <summary>The id the label points at, for the derived control to put on its element.</summary>
    protected string FieldId => Id ?? "f-" + Slug(BoundMemberName() ?? Label ?? AccessibleLabel ?? "field");

    private string? BoundMemberName() =>
        Bind?.Body is MemberExpression member ? member.Member.Name : null;

    private static string Slug(string text)
    {
        var slug = new char[text.Length];
        var n = 0;

        foreach (var c in text)
        {
            slug[n++] = char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-';
        }

        return new string(slug, 0, n).Trim('-');
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var control = Control();
        var validation = ValidationFor();

        // Nothing to wrap it in. Keeps a bare control's markup exactly as it was, which is what a control
        // in a table cell or a toolbar wants — and means adding this base changed no rendered output for
        // any call site that had no label, no hint and nothing to validate.
        if (Label is null && Hint is null && validation is null && Error is null)
        {
            return control;
        }

        return Div.Class("fieldset")[
            // RaskMarkup.Label, qualified: this type has a Label PROPERTY, which shadows the <label> chain
            // entry of the same name — the "Color Color" problem. UiCheckbox avoids it by calling its own
            // property Text; a form field's label should be called Label, so the entry is reached through
            // the base that declares it instead.
            Label is null ? null : RaskMarkup.Label.For(FieldId).Class("fieldset-legend")[Label],
            control,
            // A SIBLING of the control, which is what daisyUI's `.validator ~ .validator-hint` requires.
            validation,
            // daisyUI's own class, so the reveal-on-invalid behaviour is the library's rather than a second
            // implementation of it.
            Error is null ? null : P.Class("validator-hint")[Error],
            Hint is null ? null : P.Class("label")[Hint]
        ];
    }
}
