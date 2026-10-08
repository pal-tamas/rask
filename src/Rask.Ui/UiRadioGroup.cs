using System.Linq.Expressions;
using Rask.Core.Components;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's radio group: a set of choices where exactly one may be chosen, bound as ONE field.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ui.RadioGroup.Bind(() =&gt; model.Plan).Label("Plan")[Ui.Radio.Value(Plan.Free).Label("Free"), …]</c>
/// binds the group's value, of any type. The radios are its children; each one's <c>Value</c> is what the
/// member becomes when it is chosen. <c>.Value(x)</c> with <c>OnChange</c> leaves the value with the parent.
/// </para>
/// <para>
/// <see cref="Variant" /> draws the same radios as one segmented strip, cards, pills or buttons. Every one of
/// them keeps a real <c>&lt;input type="radio"&gt;</c>, so the arrow keys move and choose, wrapping at the
/// ends, with no script.
/// </para>
/// </remarks>
public sealed partial class UiRadioGroup<T> : Component, IFormControl<T>, IUiFormControl
{
    private string? _ownId;

    /// <summary>The group's heading, drawn over it in a <see cref="UiField" />.</summary>
    public string? Label { get; set; }

    /// <summary>Help text between the heading and the radios.</summary>
    public string? Description { get; set; }

    /// <summary>One per row, or a segmented strip, cards, pills or buttons.</summary>
    public Ui.RadioGroupVariant? Variant { get; set; }

    /// <summary>How tall a segmented group is. 40px unless this says smaller.</summary>
    public Ui.RadioGroupSize? Size { get; set; }

    /// <summary><see langword="false" /> draws cards without their dot: the border alone says which is chosen.</summary>
    public bool? Indicator { get; set; }

    /// <summary>Draws the error state. A bound group is invalid on its own while its form holds a message for it.</summary>
    public bool? Invalid { get; set; }

    /// <summary>
    ///     The <c>name</c> every radio of the group posts under: their <c>&lt;input&gt;</c>s' own attribute. The
    ///     group's id unless set.
    /// </summary>
    public string? Name { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <summary>Classes for the group: <c>flex-col</c> stacks cards, <c>max-sm:flex-col</c> only on a phone.</summary>
    public string? Class { get; set; }

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

    string IUiFieldControl.ControlId => Id is null && Bind is null && Label is null
        ? _ownId ??= UiFieldId.Own(UiInstanceCounter.Next())
        : UiFieldId.Derive(Id, Bind, Label);

    LambdaExpression? IUiFieldControl.Bound => Bind;

    string? IUiFormControl.DescriptionTrailing => null;

    string? IUiFormControl.Badge => null;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        var field = UiWithField.For(this);
        var variant = Variant ?? Ui.RadioGroupVariant.Default;
        var size = Size ?? Ui.RadioGroupSize.Base;
        var scope = new UiRadioScope(
            field.ControlId,
            Name ?? field.ControlId,
            variant,
            size,
            Indicator != false,
            field.Invalid,
            current,
            value => value is T typed ? UiFormCommit.CommitAsync(this, accessor, context, typed) : Task.CompletedTask);

        // The radios are grouped by name already; the GROUP has a name of its own to announce — the heading.
        var aria = new Dictionary<string, string?>(field.Aria, StringComparer.Ordinal);
        if (Label is not null)
        {
            aria["labelledby"] = field.LabelId;
        }

        return field.Wrap(
            Div.Id(field.ControlId)
                .Role("radiogroup")
                .Class(UiClass.Compose(Look(variant, size), Class))
                .Aria(aria)
                .Data(Marker(variant), "")[
                Context.Provide(scope)[Children ?? []]
            ]);
    }

    // In the default list a row keeps 12px from the next, 16px when it carries a description.
    private static string Look(Ui.RadioGroupVariant variant, Ui.RadioGroupSize size) => (variant, size) switch
    {
        (Ui.RadioGroupVariant.Segmented, Ui.RadioGroupSize.Sm) =>
            "flex -my-px rounded-lg p-[3px] bg-zinc-800/5 dark:bg-white/10",
        (Ui.RadioGroupVariant.Segmented, _) => "flex rounded-lg p-1 bg-zinc-800/5 dark:bg-white/10",
        (Ui.RadioGroupVariant.Cards, _) => "flex gap-3",
        (Ui.RadioGroupVariant.Pills, _) => "flex flex-wrap gap-3",
        (Ui.RadioGroupVariant.Buttons, _) => "flex gap-2",
        _ => "[&>[data-ui-field]:not(:last-child)]:mb-3 "
             + "[&>[data-ui-field]:has(>[data-ui-description]):not(:last-child)]:mb-4",
    };

    private static string Marker(Ui.RadioGroupVariant variant) => variant switch
    {
        Ui.RadioGroupVariant.Segmented => "ui-radio-group-segmented",
        Ui.RadioGroupVariant.Cards => "ui-radio-group-cards",
        Ui.RadioGroupVariant.Pills => "ui-radio-group-pills",
        Ui.RadioGroupVariant.Buttons => "ui-radio-group-buttons",
        _ => "ui-radio-group",
    };
}
