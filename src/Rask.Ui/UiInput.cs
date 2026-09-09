using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A text field.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI's <c>input</c>. <see cref="Tone" /> colours the border, which is how a field says it is in
/// error without a second element; <see cref="UiValidator" /> is the version that says why.
/// </para>
/// <para>
/// A form control: <c>.Bind(() =&gt; model.Email)</c> two-way binds and drives the surrounding
/// <c>Form</c>'s validation, or <see cref="Value" /> with <see cref="OnChange" /> lets the parent own
/// it. The opening step fixes both the type argument and the mode, so a call site reads
/// <c>UiInput.Bind(…).Label(…)</c> or <c>UiInput.Value(…).Label(…)</c>.
/// </para>
/// <para>
/// <typeparamref name="T" /> is whatever the field holds — a <c>string</c>, an <c>int</c>, a
/// <c>DateOnly</c>. The parse in both directions is the framework's, not this component's: it forwards
/// to <c>Rask.Core</c>'s <c>Input&lt;T&gt;</c>, which is itself an <c>IFormControl&lt;T&gt;</c>.
/// </para>
/// </remarks>
public sealed partial class UiInput<T> : Component, IFormControl<T>
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    /// <remarks>Rendered as <c>aria-label</c>: a placeholder is not a name, it vanishes when typing starts.</remarks>
    public required string Label { get; set; }

    public string? Placeholder { get; set; }

    public InputType? Type { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    public T? Value { get; set; }

    /// <inheritdoc />
    public Action<T>? OnChange { get; set; }

    /// <inheritdoc />
    public Func<T, Task>? OnChangeAsync { get; set; }

    /// <inheritdoc />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<T>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<T>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Action<T>? AfterBind { get; set; }

    /// <inheritdoc />
    public Func<T, Task>? AfterBindAsync { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Bound and controlled are different chain TYPES, not two settings on one — Bind and Value are
        // mutually exclusive openings — so each is built as its own complete expression.
        if (Bind is { } bind)
        {
            return Input
                .Bind(bind)
                .Validate(Validate)
                .ValidateAsync(ValidateAsync)
                .AfterBind(AfterBind)
                .AfterBindAsync(AfterBindAsync)
                .Type(Type)
                .Placeholder(Placeholder ?? string.Empty)
                .Aria(Aria())
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Input
            .Value(Value)
            .OnChange(OnChange)
            .OnChangeAsync(OnChangeAsync)
            .Type(Type)
            .Placeholder(Placeholder ?? string.Empty)
            .Aria(Aria())
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    // aria-invalid is what makes daisyUI reveal a following UiValidator, and what a screen reader
    // needs: a field that is visibly red and says nothing is half a message. It is OMITTED rather than
    // nulled — a null renders the attribute valueless, and a valueless aria-invalid reads as "true",
    // which would mark every field in the kit invalid.
    private Dictionary<string, string?> Aria() =>
        Tone == UiTone.Error
            ? new Dictionary<string, string?> { ["label"] = Label, ["invalid"] = "true" }
            : new Dictionary<string, string?> { ["label"] = Label };

    private string BoxClass() =>
        UiClass.Compose(
            "input validator",
            Tone is { } tone ? UiClassNames.InputTone(tone) : "",
            Size is { } size ? UiClassNames.InputSize(size) : "",
            Variant is { } variant ? UiClassNames.InputVariant(variant) : "",
            Class);
}
