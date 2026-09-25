using Rask.Core.Forms;

namespace Rask;

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
/// fixes both the type argument and the mode, so a call site reads <c>Ui.Input.Bind(…).Label(…)</c> or
/// <c>Ui.Input.Value(…).Label(…)</c>.
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

    /// <summary>An icon inside the box, before what is typed — a magnifier on a search, a badge on a key.</summary>
    /// <remarks>
    ///     daisyUI's icon input puts the icon INSIDE the box, which is the same room a floating caption rises
    ///     through — so a field with an icon keeps its label above it as a legend. See <see cref="Floating" />.
    /// </remarks>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon inside the box, after what is typed.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>A shortcut shown at the end of the box — <c>"⌘K"</c> on a search field.</summary>
    /// <remarks>Decoration: it says which key focuses the field, and the page is what binds that key.</remarks>
    public string? Kbd { get; set; }

    /// <summary>Adds a button inside the box that empties the field.</summary>
    /// <remarks>Shown only when there is something to clear, and never on a disabled field.</remarks>
    public bool? Clearable { get; set; }

    // Anything that lives INSIDE the box needs the box to be a container rather than the <input> itself.
    private bool HasAffordance =>
        Icon is not null || IconTrailing is not null || Kbd is not null || Clearable == true;

    // A floating caption rises through the inside of the box, which is exactly where an icon, a shortcut or a
    // clear button sits — so a field with any of those keeps its label above it. Stating Floating(true) beside
    // one is a contradiction rather than a preference, and this resolves it the way that keeps both readable.
    private protected override bool FloatsLabel => Floating != false && !HasAffordance;

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
    public Callback<string> OnInput { get; set; }

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
    protected override Component Control() =>
        HasAffordance ? Boxed(Field()) : Field();

    // daisyUI's icon input: the BOX is the container and the <input> inside it is bare. A <div> rather than a
    // <label>, because the field already has one — a wrapping label implicitly names the input it holds, and a
    // second name is what produced "Email Email" the last time this happened.
    private Component Boxed(Component field) =>
        Div.Class(BoxClass())[
            Icon is { } icon ? Ui.Icon.Name(icon).Class("size-4 shrink-0 opacity-60") : null,
            field,
            Kbd is { } kbd ? Span.Class("kbd kbd-sm shrink-0")[kbd] : null,
            Clearable == true && !string.IsNullOrEmpty(Current()?.ToString()) && Disabled != true
                ? Button
                    .Type("button")
                    .Class("shrink-0 opacity-60 hover:opacity-100")
                    .Aria("label", "Clear " + (Label ?? AccessibleLabel ?? "field"))
                    .OnClick(ClearAsync)[
                    Ui.Icon.Name(Ui.IconName.Close).Class("size-4")
                ]
                : null,
            IconTrailing is { } trailing ? Ui.Icon.Name(trailing).Class("size-4 shrink-0 opacity-60") : null
        ];

    // What the field is showing, which in BOUND mode is the model's, not Value — that one is null there, and
    // reading it would mean a bound field never offered to clear anything.
    private T? Current() =>
        Bind is { } bind && ExpressionAccessor.Parse(bind).Getter() is T v ? v : Value;

    private async Task ClearAsync()
    {
        var self = (IFormControl<T>)this;
        if (Bind is { } bind)
        {
            var acc = Rask.Core.Forms.ExpressionAccessor.Parse(bind);
            acc.Setter(default!);
            await Rask.Core.Forms.BindingHelpers
                .NotifyAndValidateFieldAsync(Rask.Core.Forms.BindingHelpers.ResolveBindingContext(acc.Target), acc.Field)
                .ConfigureAwait(false);
            await self.InvokeAfterBindAsync(default!).ConfigureAwait(false);
            return;
        }

        await self.InvokeOnChangeAsync(default!).ConfigureAwait(false);
    }

    private Component Field()
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
                .Class(FieldClass());
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
            .Class(FieldClass());
    }

    // What the <input> itself wears. Inside a box the box carries the look, so the input keeps only its
    // validator hook and the growth that fills the room left beside the icons.
    private string FieldClass() =>
        HasAffordance ? "validator grow" : BoxClass();

    private string BoxClass() =>
        UiClass.Compose(
            "input validator",
            Tone is { } tone ? UiClassNames.InputTone(tone) : "",
            Size is { } size ? UiClassNames.InputSize(size) : "",
            Variant is { } variant ? UiClassNames.InputVariant(variant) : "",
            Class);
}
