using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A text field.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI's <c>input</c>, with the label, the hint and the validation message coming from
/// <see cref="UiFormField{T}" /> — so a field is one component rather than three siblings a call site has
/// to keep associated. <c>UiFormField&lt;T&gt;.Tone</c> colours the border, which is how a field says it
/// is in error without a second element, and the message under it says why.
/// </para>
/// <para>
/// A form control: <c>.Bind(() =&gt; model.Email)</c> two-way binds and drives the surrounding
/// <c>Form</c>'s validation, or <c>Value</c> with <c>OnChange</c> lets the parent own it. The opening step
/// fixes both the type argument and the mode, so a call site reads <c>UiInput.Bind(…).Label(…)</c> or
/// <c>UiInput.Value(…).Label(…)</c>.
/// </para>
/// <para>
/// <typeparamref name="T" /> is whatever the field holds — a <c>string</c>, an <c>int</c>, a
/// <c>DateOnly</c>. The parse in both directions is the framework's, not this component's: it forwards
/// to <c>Rask.Core</c>'s <c>Input&lt;T&gt;</c>, which is itself an <c>IFormControl&lt;T&gt;</c>.
/// </para>
/// </remarks>
public sealed partial class UiInput<T> : UiFormField<T>
{
    /// <summary>
    ///     Shown in the empty field — for a field whose label does not float. Ignored while the label floats: the
    ///     label IS the placeholder then, so put guidance in <c>Hint</c> instead. See <see cref="Floating" />.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     Whether a <c>Label</c> floats: sits in the field until there is content, then rises above it. On
    ///     unless this is <see langword="false" />, which draws the label above the field instead.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     While the label floats it is also the placeholder, and an explicit <see cref="Placeholder" /> is
    ///     ignored. daisyUI raises the caption once the control stops showing its placeholder, so the field needs
    ///     one — and a DIFFERENT one would sit in the box in the label's place, hiding the one thing saying what
    ///     the field is for until somebody focused it. Guidance about the value belongs in <c>Hint</c>, under
    ///     the field, where it stays visible while typing.
    ///     </para>
    /// </remarks>
    public bool? Floating { get; set; }

    /// <inheritdoc />
    private protected override bool FloatsLabel => Floating != false;

    private string PlaceholderText => Label is not null && FloatsLabel ? Label : Placeholder ?? string.Empty;

    public InputType? Type { get; set; }

    /// <summary>
    ///     Runs on every keystroke, with the raw text.
    /// </summary>
    /// <remarks>
    ///     Distinct from <c>OnChange</c>, which fires when the field is done being edited. This one is for
    ///     a search box that filters as you type or a counter under a textarea — and it is raw
    ///     <c>string</c> rather than <typeparamref name="T" /> on purpose: mid-word input is very often not
    ///     yet a valid <typeparamref name="T" />, so parsing it per keystroke would either throw or lie.
    /// </remarks>
    public Callback<string>? OnInput { get; set; }

    /// <summary>The lowest accepted value, for a number or a date. The attribute, verbatim.</summary>
    /// <remarks>
    ///     A string because that is what the attribute is: <c>min="0"</c>, <c>min="2026-01-01"</c>,
    ///     <c>min="09:00"</c>. Typing it per input type would need a property per type.
    /// </remarks>
    public string? Min { get; set; }

    /// <inheritdoc cref="Min" />
    public string? Max { get; set; }

    /// <summary>The granularity the value snaps to — <c>step="0.01"</c>, <c>step="any"</c>.</summary>
    public string? Step { get; set; }

    /// <summary>The most characters that can be typed.</summary>
    /// <remarks>
    ///     A hard stop, not validation: the browser refuses the keystroke, so nothing reports why. Say so
    ///     in <c>Hint</c> as well, or use a validation rule if the reader needs to be told.
    /// </remarks>
    public int? MaxLength { get; set; }

    /// <summary>Takes focus when the page arrives.</summary>
    /// <remarks>
    ///     One per page, and only where the field IS the page's purpose — a search page, a login. It moves
    ///     the caret out from under a reader who was about to read the page, and on a phone it opens the
    ///     keyboard over the content.
    /// </remarks>
    public bool? Autofocus { get; set; }

    /// <summary>The id of a <c>&lt;datalist&gt;</c> offering suggestions.</summary>
    public string? List { get; set; }

    /// <summary>
    ///     A handle to the rendered element, for the code that has to reach it — focus, scroll, measure.
    /// </summary>
    /// <remarks>
    ///     Hold it in a field (<c>ElementRef.New()</c>) and pass it to <c>IJSRuntime</c>. Without this a
    ///     call site that needed to focus its own field had to render a raw element and a class string.
    /// </remarks>
    public ElementRef? Ref { get; set; }

    /// <summary>
    ///     The <c>name</c> the value posts under from a plain HTML form. Defaults to the bound member's name.
    /// </summary>
    /// <remarks>
    ///     Without it a kit field inside a <c>&lt;form&gt;</c> that posts its data — rather than handing a
    ///     model to C# — contributes nothing to the submission. The same prop <c>UiSelect</c> has.
    /// </remarks>
    public string? Name { get; set; }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override Component Control()
    {
        // Bound and controlled are different chain TYPES, not two settings on one — Bind and Value are
        // mutually exclusive openings — so each is built as its own complete expression.
        if (Bind is { } bind)
        {
            return Input
                .Bind(bind)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Id(FieldId)
            .Name(Name)
            .Ref(Ref)
            .OnInput(OnInput)
            .Min(Min)
            .Max(Max)
            .Step(Step)
            .MaxLength(MaxLength)
            .Autofocus(Autofocus == true)
            .List(List)
            .Type(Type)
                .Placeholder(PlaceholderText)
                .Aria(ControlAria())
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Input
            .Value(Value)
            .OnChange(OnChange)
            .Id(FieldId)
            .Name(Name)
            .Ref(Ref)
            .OnInput(OnInput)
            .Min(Min)
            .Max(Max)
            .Step(Step)
            .MaxLength(MaxLength)
            .Autofocus(Autofocus == true)
            .List(List)
            .Type(Type)
            .Placeholder(PlaceholderText)
            .Aria(ControlAria())
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    private string BoxClass() =>
        UiClass.Compose(
            "input validator",
            Tone is { } tone ? UiClassNames.InputTone(tone) : "",
            Size is { } size ? UiClassNames.InputSize(size) : "",
            Variant is { } variant ? UiClassNames.InputVariant(variant) : "",
            Class);
}
