using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A set of choices where exactly one may be picked, bound as ONE field.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's radio group. <see cref="UiRadio" /> is one option and binds its own <c>bool</c>; this binds the
/// GROUP's value, so <c>.Bind(() =&gt; model.Plan)</c> is the whole field — which is what a form actually has.
/// It is the control to reach for; a bare <see cref="UiRadio" /> is for a set the page assembles itself.
/// </para>
/// <para>
/// <see cref="Layout" /> is the same set of looks Flux gives it: a list, cards with room for a description,
/// pills, buttons, or one joined segmented strip. Every one of them keeps a real
/// <c>&lt;input type="radio"&gt;</c> inside its label, so the browser's own grouping, the arrow keys, the space
/// bar and the form post all still work — the look is CSS reading the input's own <c>:checked</c> state, never
/// a <c>&lt;button&gt;</c> pretending to be a choice.
/// </para>
/// </remarks>
public sealed partial class UiRadioGroup<T> : UiFormField<T>
{
    /// <summary>The choices: the value stored, and the words shown.</summary>
    public required IReadOnlyList<(T Value, string Text)> Options { get; set; }

    /// <summary>How the choices are laid out. A list, unless this says otherwise.</summary>
    public UiChoiceLayout? Layout { get; set; }

    /// <summary>A second line under a choice's words, saying what picking it means.</summary>
    /// <remarks>Drawn only by <see cref="UiChoiceLayout.Cards" />, which is the layout with room for it.</remarks>
    public Fn<T, string?>? OptionDescription { get; set; }

    /// <summary>Marks choices that cannot be picked.</summary>
    public Fn<T, bool>? OptionDisabled { get; set; }

    /// <summary>
    ///     The shared <c>name</c> that makes the radios exclusive, and the field's name in a plain form post.
    /// </summary>
    /// <remarks>The field's own id unless this says otherwise, which is already unique on the page.</remarks>
    public string? Name { get; set; }

    /// <inheritdoc />
    protected override Component Control()
    {
        var layout = Layout ?? UiChoiceLayout.List;
        var acc = Bind is { } bind ? ExpressionAccessor.Parse(bind) : null;
        var ctx = acc is null ? null : BindingHelpers.ResolveBindingContext(acc.Target);
        if (acc is not null)
        {
            // Every render, deliberately: passing the collapsed validator each time is also what clears a stale
            // rule when the consumer stops supplying one.
            ((IFormControl<T>)this).RegisterValidator(acc, ctx);
        }

        var current = Current();
        var group = Name ?? FieldId;

        // A radiogroup rather than a bare div: the choices are inputs the browser already groups by name, but
        // the GROUP has a name of its own to announce — the field's label — and nothing else would carry it.
        var aria = ControlAria();
        if (Label is not null)
        {
            aria["labelledby"] = LabelId;
            aria.Remove("label");
        }

        return Div
            .Id(FieldId)
            .Role("radiogroup")
            .Class(UiClass.Compose(UiChoice.ContainerClass(layout), Class))
            .Aria(aria)[
            Options.Select((option, index) => Choice(option, index, layout, group, current, acc, ctx))
        ];
    }

    private Component Choice(
        (T Value, string Text) option,
        int index,
        UiChoiceLayout layout,
        string group,
        T? current,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx)
    {
        var off = Disabled == true || OptionDisabled?.Invoke(option.Value) == true;
        var picked = EqualityComparer<T>.Default.Equals(option.Value, current);
        var description = layout == UiChoiceLayout.Cards ? OptionDescription?.Invoke(option.Value) : null;

        var box = Input
            .Of<bool>()
            .Checked(picked)
            .Id(FieldId + "-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Type(InputType.Radio)
            .Name(group)
            .Disabled(off)
            .Class(UiChoice.ShowsBox(layout)
                ? UiClass.Compose(
                    "radio mt-0.5",
                    Tone is { } tone ? UiClassNames.RadioTone(tone) : "",
                    Size is { } size ? UiClassNames.RadioSize(size) : "")
                : UiChoice.HiddenBoxClass)
            // A radio only ever reports true: choosing one fires no change on the option it deselected, so the
            // value to commit is this option's, not whatever the event carried.
            .OnChange(_ => CommitAsync(option.Value, acc, ctx));

        return RaskMarkup.Label.Key(index).Class(UiChoice.ChoiceClass(layout))[
            box,
            description is null
                ? Span[option.Text]
                : Div.Class("flex min-w-0 flex-col gap-0.5")[
                    Span.Class("text-sm font-medium")[option.Text],
                    Span.Class("text-xs opacity-60")[description]
                ]
        ];
    }

    private async Task CommitAsync(T value, ExpressionAccessor.Accessor? acc, EditContext? ctx)
    {
        var self = (IFormControl<T>)this;
        if (acc is not null)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, acc.Field).ConfigureAwait(false);
            await self.InvokeAfterBindAsync(value).ConfigureAwait(false);
        }
        else
        {
            await self.InvokeOnChangeAsync(value).ConfigureAwait(false);
        }
    }

    private T? Current() =>
        Bind is { } bind && ExpressionAccessor.Parse(bind).Getter() is T v ? v : Value;
}
