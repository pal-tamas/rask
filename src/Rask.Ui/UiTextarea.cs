using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's textarea: <c>Ui.Textarea.Bind(() =&gt; m.Notes).Label("Order notes")</c>.
/// </summary>
/// <remarks>
/// A form control like <see cref="UiInput{T}" />: <c>Bind</c> two-way binds and drives the form's validation,
/// <c>Value</c> with <c>OnChange</c> leaves the value with the parent, and <c>Label</c> / <c>Description</c> /
/// <c>Badge</c> wrap it in a <see cref="UiField" />. The element is the <c>&lt;textarea&gt;</c> itself
/// (<c>data-ui-textarea</c>), four lines tall and resizable vertically unless told otherwise.
/// </remarks>
public sealed partial class UiTextarea<T> : Component, IFormControl<T>, IUiFormControl
{
    private const string Look =
        "block w-full p-3 rounded-lg text-base sm:text-sm shadow-xs disabled:shadow-none data-invalid:shadow-none";

    /// <inheritdoc cref="IUiFormControl.Label" />
    public string? Label { get; set; }

    /// <inheritdoc cref="IUiFormControl.Description" />
    public string? Description { get; set; }

    /// <inheritdoc cref="IUiFormControl.DescriptionTrailing" />
    public string? DescriptionTrailing { get; set; }

    /// <inheritdoc cref="IUiFormControl.Badge" />
    public string? Badge { get; set; }

    /// <summary>Shown while the textarea is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>
    ///     How many lines it shows — 4 unless set — or <see cref="UiTextareaRows.Auto" /> to grow with what is typed.
    /// </summary>
    /// <remarks>
    ///     Auto is CSS's <c>field-sizing: content</c>: no script, and where an engine has not shipped it the box
    ///     keeps the browser's two lines and scrolls.
    /// </remarks>
    public UiTextareaRows? Rows { get; set; }

    /// <summary>Which way the reader may drag it bigger. Vertically unless this says otherwise.</summary>
    public Ui.TextareaResize? Resize { get; set; }

    /// <inheritdoc cref="UiInput{T}.Invalid" />
    public bool? Invalid { get; set; }

    /// <inheritdoc cref="UiInput{T}.Disabled" />
    public bool? Disabled { get; set; }

    /// <inheritdoc cref="UiInput{T}.ReadOnly" />
    public bool? ReadOnly { get; set; }

    /// <summary>Classes for the <c>&lt;textarea&gt;</c>.</summary>
    public string? Class { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

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
    ///     Writes the model as the reader types, after a 150 ms pause, and validates then:
    ///     <c>Ui.Textarea.Bind(() =&gt; m.Title).Live()</c>. Flux's <c>wire:model.live</c>. Without it a bound
    ///     field says nothing until the next action, and its value travels with that — Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live
    {
        get => _timing.Live;
        set => _timing.Live = value;
    }

    /// <summary>
    ///     Binds when the reader leaves the field, and validates then:
    ///     <c>Ui.Textarea.Bind(() =&gt; m.Name).Blur()</c>. Flux's <c>wire:model.blur</c>.
    /// </summary>
    public bool? Blur
    {
        get => _timing.Blur;
        set => _timing.Blur = value;
    }

    /// <summary>
    ///     Binds as the reader types, once typing has paused for this long, and validates then:
    ///     <c>Ui.Textarea.Bind(() =&gt; m.Name).Debounce(300.Milliseconds)</c>. Flux's
    ///     <c>wire:model.live.debounce.300ms</c>.
    /// </summary>
    public TimeSpan? Debounce
    {
        get => _timing.Debounce;
        set => _timing.Debounce = value;
    }

    private BindTiming _timing;

    /// <inheritdoc cref="UiInput{T}.OnInput" />
    public Callback<string> OnInput { get; set; }

    // Nothing names it — no Id, no bound member, no label: an id of its own, not one every such textarea shares.
    private string? _ownId;

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var field = UiWithField.For(this);
        var rows = Rows ?? 4;

        // Bind and Value are the two openings of Core's textarea, and both hand back the same element.
        var textarea = Bind is { } bind
            ? Textarea.Bind(bind).Validate(Validate).AfterBind(AfterBind).Live(Live).Blur(Blur).Debounce(Debounce)
            : Textarea.Value(Value).OnChange(OnChange);

        return field.Wrap(textarea
            .Id(field.ControlId)
            .OnInput(OnInput)
            .Placeholder(Placeholder)
            .Rows(rows.Count)
            .Disabled(Disabled == true)
            .ReadOnly(ReadOnly == true)
            .Aria(field.Aria)
            .Attributes(UiInputLook.TextareaMarks(field.Invalid))
            .Class(UiClass.Compose(
                Look,
                UiInputLook.Outline,
                rows.Count is null ? "field-sizing-content" : null,
                ResizeClass(Resize ?? Ui.TextareaResize.Vertical),
                Class)));
    }

    private static string ResizeClass(Ui.TextareaResize resize) => resize switch
    {
        Ui.TextareaResize.Horizontal => "resize-x",
        Ui.TextareaResize.Both => "resize",
        Ui.TextareaResize.None => "resize-none",
        _ => "resize-y",
    };
}
