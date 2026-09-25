namespace Rask;

/// <summary>
/// A multi-line text field.
/// </summary>
/// <remarks>
/// A form control, like every input in the kit: <c>.Bind(() =&gt; model.Notes)</c> two-way binds and
/// drives the surrounding <c>Form</c>'s validation, or <c>Value</c> with <c>OnChange</c>
/// leaves the value with the parent. The opening step fixes both the type argument and the mode.
/// </remarks>
public sealed partial class UiTextarea<T> : UiFormField<T>
{
    /// <inheritdoc cref="UiInput{T}.Placeholder" />
    public string? Placeholder { get; set; }

    /// <inheritdoc cref="UiInput{T}.Floating" />
    public bool? Floating { get; set; }

    /// <inheritdoc />
    private protected override bool FloatsLabel => Floating != false;

    private string PlaceholderText => Label is not null && FloatsLabel ? Label : Placeholder ?? string.Empty;

    public int? Rows { get; set; }

    /// <inheritdoc cref="UiInput{T}.OnInput" />
    public Callback<string> OnInput { get; set; }

    /// <inheritdoc cref="UiInput{T}.Name" />
    public string? Name { get; set; }

    /// <summary>
    ///     daisyUI defines only <see cref="Ui.Variant.Ghost" /> for a text control — the borderless form
    ///     that shows its edges on focus. The rest draw the default rather than a class that does nothing.
    /// </summary>

    /// <summary>
    ///     Which way the reader may drag the box bigger. Vertically, unless this says otherwise.
    /// </summary>
    /// <remarks>
    ///     <see cref="Ui.Resize.None" /> is for a box in a layout the extra height would break — a row in a
    ///     table, a cell in a grid. Taking the handle away is a real cost to somebody writing a long answer,
    ///     so it wants a reason; <see cref="AutoSize" /> is usually the better one.
    /// </remarks>
    public Ui.Resize? Resize { get; set; }

    /// <summary>
    ///     Grows the box to fit what is typed, instead of scrolling inside a fixed height.
    /// </summary>
    /// <remarks>
    ///     CSS, not script: `field-sizing: content` is the platform's own answer, so it works with no runtime
    ///     and on a prerendered page. Where an engine has not shipped it the box keeps its <see cref="Rows" />
    ///     and scrolls, which is exactly what it does today — the feature degrades to the current behaviour
    ///     rather than to a broken one. <see cref="Rows" /> becomes the SMALLEST it will be.
    /// </remarks>
    public bool? AutoSize { get; set; }

    /// <inheritdoc />
    protected override Component Control()
    {
        if (Bind is { } bind)
        {
            return Textarea
                .Bind(bind)
                .Id(FieldId)
                .Name(Name)
                .OnInput(OnInput)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Placeholder(PlaceholderText)
                .Rows(Rows ?? 3)
                .Aria(ControlAria())
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Textarea
            .Value(Value)
            .Id(FieldId)
            .Name(Name)
            .OnInput(OnInput)
            .OnChange(OnChange)
            .Placeholder(PlaceholderText)
            .Rows(Rows ?? 3)
            .Aria(ControlAria())
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    private string BoxClass() =>
        UiClass.Compose(
            "textarea validator",
            AutoSize == true ? "ui-textarea-auto" : "",
            Resize is { } resize ? UiClassNames.Resize(resize) : "",
            Tone is { } tone ? UiClassNames.TextareaTone(tone) : "",
            Variant is { } variant ? UiClassNames.TextareaVariant(variant) : "",
            Size is { } size ? UiClassNames.TextareaSize(size) : "",
            Class);
}
