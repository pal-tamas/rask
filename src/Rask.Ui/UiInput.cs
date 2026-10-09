using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's input: <c>Ui.Input.Bind(() =&gt; m.Email).Label("Email")</c>.
/// </summary>
/// <remarks>
/// <para>
/// A form control. <c>.Bind(() =&gt; model.Age)</c> two-way binds — <typeparamref name="T" /> is the member's
/// type, so the value is parsed and written back for you — and drives the surrounding form's validation;
/// <c>.Value(x)</c> with <c>OnChange</c> leaves the value with the parent; <c>.Of&lt;string&gt;()</c> opens one
/// with no value yet. The binding itself is <c>Rask.Core</c>'s <c>Input&lt;T&gt;</c>, which this draws.
/// </para>
/// <para>
/// <c>Label</c>, <c>Description</c> and <c>DescriptionTrailing</c> wrap it in a
/// <see cref="UiField" /> with its <see cref="UiError" />, as Flux's shorthand does. Without them it is the
/// input alone, to be placed in a <c>Ui.Field</c> or a <c>Ui.InputGroup</c> of your own.
/// </para>
/// <para>
/// The root is a wrapper <c>div</c> (<c>data-ui-input</c>) holding the <c>&lt;input&gt;</c>
/// (<c>data-ui-control</c>) and whatever sits inside the box: <c>Class</c> goes to the wrapper,
/// <c>InputClass</c> to the input.
/// </para>
/// </remarks>
public sealed partial class UiInput<T> : Component, IFormControl<T>, IUiFormControl
{
    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <summary>Shown while the input is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>How tall it is. 40px unless this says smaller.</summary>
    public Ui.InputSize? Size { get; set; }

    /// <summary>Outlined, or filled for a value that is shown rather than asked for.</summary>
    public Ui.InputVariant? Variant { get; set; }

    /// <summary>Takes no input and is skipped by the keyboard.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Shows its value and takes focus, but cannot be edited.</summary>
    public bool? ReadOnly { get; set; }

    /// <summary>
    ///     Draws the error state. A bound input is invalid on its own while its form holds a message for it.
    /// </summary>
    public bool? Invalid { get; set; }

    /// <summary>A file input takes several files.</summary>
    public bool? Multiple { get; set; }

    /// <summary>
    ///     The pattern the text is held to — <c>9</c> a digit, <c>a</c> a letter, <c>*</c> either, anything else as
    ///     written: <c>"(999) 999-9999"</c>. For an input over a <c>string</c>.
    /// </summary>
    /// <remarks>
    ///     The runtime holds each keystroke to it (<c>data-rask-mask</c>), and the value drawn and the value
    ///     committed are laid into it here, so a value that arrives unshaped is shown shaped.
    /// </remarks>
    public string? Mask { get; set; }

    /// <summary>A Heroicon inside the box, before what is typed — or content of your own there.</summary>
    public UiInputIcon? Icon { get; set; }

    /// <summary>A Heroicon inside the box, after what is typed — or content of your own, a button usually.</summary>
    public UiInputIcon? IconTrailing { get; set; }

    /// <summary>A shortcut shown at the end of the box — <c>"⌘K"</c>. A hint: the page binds the key.</summary>
    public string? Kbd { get; set; }

    /// <summary>A button at the end of the box that empties it, shown while there is something to clear.</summary>
    public bool? Clearable { get; set; }

    /// <summary>A button at the end of the box that copies what it holds, and shows a tick for two seconds.</summary>
    public bool? Copyable { get; set; }

    /// <summary>A button at the end of a password input that shows what was typed, and hides it again.</summary>
    public bool? Viewable { get; set; }

    /// <summary>Renders a <c>&lt;button&gt;</c> drawn as the input, showing <see cref="Placeholder" />.</summary>
    public Ui.InputAs? As { get; set; }

    /// <summary>Classes for the <c>&lt;input&gt;</c> itself, where <see cref="Class" /> reaches the wrapper.</summary>
    public string? InputClass { get; set; }

    /// <summary>
    ///     Attributes for the <c>&lt;input&gt;</c> itself, written as given — what Flux forwards to it:
    ///     <c>.Attributes(("aria-label", "Search keys"))</c>, <c>("required", "")</c>, <c>("autocomplete", "email")</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Attributes { get; set; }

    /// <summary>Classes for the wrapper <c>div</c>: widths and margins.</summary>
    public string? Class { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <summary>Which input this is — email, password, date, file. Derived from <typeparamref name="T" /> when unset.</summary>
    public InputType? Type { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public T? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<T> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<T>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<T> AfterBind { get; set; }

    /// <summary>
    ///     Binds when the reader leaves the field rather than on every keystroke, and validates then:
    ///     <c>Ui.Input.Bind(() =&gt; m.Name).Blur()</c>. Flux's <c>wire:model.blur</c>.
    /// </summary>
    public bool? Blur
    {
        get => _timing.Blur;
        set => _timing.Blur = value;
    }

    /// <summary>
    ///     Binds once typing has paused for this long, and validates then:
    ///     <c>Ui.Input.Bind(() =&gt; m.Name).Debounce(300.Milliseconds)</c>. Flux's
    ///     <c>wire:model.live.debounce.300ms</c>; Enter, a button and leaving the field do not wait for it.
    /// </summary>
    public TimeSpan? Debounce
    {
        get => _timing.Debounce;
        set => _timing.Debounce = value;
    }

    private BindTiming _timing;

    /// <summary>Runs on every keystroke with the raw text, which is often not yet a valid <typeparamref name="T" />.</summary>
    public Callback<string> OnInput { get; set; }

    /// <summary>Runs when files are chosen in a file input.</summary>
    public Callback<IReadOnlyList<IRaskFile>> OnFiles { get; set; }

    /// <summary>Runs when the input rendered <see cref="Ui.InputAs.Button" /> is pressed.</summary>
    public Callback OnClick { get; set; }

    /// <summary>The lowest accepted value of a number or a date, as the attribute: <c>"0"</c>, <c>"2026-01-01"</c>.</summary>
    public string? Min { get; set; }

    /// <summary>The highest accepted value, as the attribute.</summary>
    public string? Max { get; set; }

    /// <summary>The granularity the value snaps to — <c>"0.01"</c>, <c>"any"</c>.</summary>
    public string? Step { get; set; }

    /// <summary>The most characters that can be typed. The browser refuses the rest and says nothing.</summary>
    public int? MaxLength { get; set; }

    /// <summary>Takes focus when the page arrives. One per page.</summary>
    public bool? Autofocus { get; set; }

    /// <summary>A handle to the <c>&lt;input&gt;</c>, for code that focuses or measures it.</summary>
    public ElementRef? Ref { get; set; }

    /// <summary>The name the value posts under from a plain HTML form. The bound member's name unless set.</summary>
    public string? Name { get; set; }

    // The id of an input nothing names — no Id, no bound member, no label. Its own, so that a label written
    // beside it, its clear button and its copy button reach THIS input and not the first such one on the page.
    private string? _ownId;

    /// <summary>The control this input is the text box of, when it is one's: its list and its bound member.</summary>
    internal UiInputHost? Host { get; private set; }

    /// <summary>Makes this input the text box of a control that drops a list under it. The kit's own chain step.</summary>
    /// <param name="host">What that control adds to the input.</param>
    internal UiInput<T> HostedBy(UiInputHost host)
    {
        Host = host;
        // What a generated step does when it writes a new value: the input is drawn again with it.
        BuilderRuntime.MarkChanged(this);

        return this;
    }

    /// <inheritdoc />
    string IUiFieldControl.ControlId => Id is null && (Host?.Bound ?? Bind) is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Host?.Bound ?? Bind, Label);

    string? IUiFormControl.Badge => null;

    LambdaExpression? IUiFieldControl.Bound => Host?.Bound ?? Bind;

    private bool IsFile => Type == InputType.File;

    private bool ShowsClear => Clearable == true && Disabled != true && ReadOnly != true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        if (As == Ui.InputAs.Button)
        {
            return field.Wrap(AsButton(field));
        }

        return field.Wrap(IsFile ? FileShell(field) : Box(field));
    }

    // Flux always draws the wrapper, icons or not: it is what `Class` and an input group reach.
    private Component Box(UiWithField field)
    {
        var leading = Icon is { } icon ? Div.Class(UiInputLook.Leading)[Glyph(icon)] : null;
        var trailing = Trailing(field.ControlId);

        // What a list hangs from, when the input is the text box of a control that drops one.
        var anchor = Host is { } host ? "anchor-name:--" + host.AnchorName : null;

        return Div.Class(UiClass.Compose(UiInputLook.Root, Class)).Data("ui-input", "").Style(anchor)[
            leading,
            Control(field, leading is not null, trailing is not null),
            trailing
        ];
    }

    private HTMLInputElement<T> Control(UiWithField field, bool leading, bool trailing)
    {
        // Bind and Value are the two openings of Core's input, and both hand back the same element.
        var input = Bind is { } bind
            ? Input.Bind(bind).Validate(Validate).AfterBind(Committed).Blur(Blur).Debounce(Debounce)
            : Input.Value(Masked(Value)).OnChange(Changed);

        if (Host is { } host)
        {
            input = input.Role("combobox").Autocomplete("off").OnKeyDown(e => host.OnKeyDown(e)).OnClick(() => host.OnClick());
        }

        if (Mask is { } mask)
        {
            input = input.Data("rask-mask", mask);
        }

        return input
            .Id(field.ControlId)
            .Name(Name)
            .Ref(Ref)
            .OnInput(OnInput)
            .Type(Viewable == true && _viewing ? InputType.Text : Type)
            .Placeholder(Placeholder ?? (ShowsClear ? " " : null))
            .Min(Min)
            .Max(Max)
            .Step(Step)
            .MaxLength(MaxLength)
            .Autofocus(Autofocus == true)
            .Disabled(Disabled == true)
            .ReadOnly(ReadOnly == true)
            .Aria(Host?.AriaOver(field.Aria) ?? field.Aria)
            .Attributes(Marks(field.Invalid))
            .Class(UiClass.Compose(
                UiInputLook.Control,
                UiInputLook.Size(Size ?? Ui.InputSize.Base),
                UiInputLook.Padding(leading, trailing),
                UiInputLook.Variant(Variant ?? Ui.InputVariant.Outline),
                InputClass));
    }

    private IReadOnlyDictionary<string, string?> Marks(bool invalid)
    {
        var marks = UiInputLook.Marks(invalid, Attributes);

        return Host?.MarksOver(marks) ?? marks;
    }

    private static Component Glyph(UiInputIcon icon) =>
        icon.Content ?? Ui.Icon.Name(icon.Name!.Value).Mini;

    // The parent's callbacks as they are, unless a mask has to see the value first.
    private Callback<T> Committed => Mask is null ? AfterBind : new Callback<T>(MaskedAfterBind);

    private Callback<T> Changed => Mask is null ? OnChange : new Callback<T>(MaskedOnChange);

    private T? Masked(T? value) => Mask is { } mask && value is string text ? (T)(object)UiInputMask.Format(mask, text) : value;

    private async Task MaskedOnChange(T value) => await OnChange.Invoke(Masked(value)!).ConfigureAwait(false);

    // Core writes what was typed and validates it; a masked input then lays it into the pattern and validates that.
    private async Task MaskedAfterBind(T value)
    {
        var masked = Masked(value)!;
        if (Bind is { } bind && !EqualityComparer<T>.Default.Equals(masked, value))
        {
            var accessor = ExpressionAccessor.Parse(bind);
            accessor.Setter(masked);
            await BindingHelpers
                .NotifyAndValidateField(BindingHelpers.ResolveBindingContext(accessor.Target), accessor.Field)
                .ConfigureAwait(false);
        }

        await AfterBind.Invoke(masked).ConfigureAwait(false);
    }
}
