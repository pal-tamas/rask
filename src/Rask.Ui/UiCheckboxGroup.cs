using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A set of choices where any number may be picked, bound as ONE field.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's checkbox group, and the sibling of <see cref="UiRadioGroup{T}" /> — the same control asked a
/// different question. It binds the COLLECTION your model already declares, so
/// <c>.Bind(() =&gt; model.Topics)</c> over a <c>List&lt;string&gt;</c> is the whole field; the controlled
/// opening is <c>Values</c>, as on <c>UiSelect</c>, because a collection expression and a bare null fit every
/// collection shape equally and one name could not tell them apart.
/// </para>
/// <para>
/// <see cref="CheckAll" /> adds the row that picks or clears everything at once. It is a real
/// <c>&lt;input&gt;</c> too, and it reports the group's state honestly: checked when everything selectable is
/// in, indeterminate while some of it is — which is what stops it claiming "all" over a half-filled list.
/// </para>
/// </remarks>
public sealed partial class UiCheckboxGroup<T> : UiFormField<ICollection<T>>
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

    /// <summary>Adds a row that picks or clears every selectable choice at once.</summary>
    public bool? CheckAll { get; set; }

    /// <summary>What that row says. "Select all" / "Clear all" unless this says otherwise.</summary>
    public string? CheckAllLabel { get; set; }

    /// <inheritdoc />
    protected override Component Control()
    {
        var layout = Layout ?? UiChoiceLayout.List;
        var acc = Bind is { } bind ? ExpressionAccessor.Parse(bind) : null;
        var ctx = acc is null ? null : BindingHelpers.ResolveBindingContext(acc.Target);
        if (acc is not null)
        {
            ((IFormControl<ICollection<T>>)this).RegisterValidator(acc, ctx);
        }

        var picked = Current();

        // A plain `group`: ARIA has no "checkboxgroup", and `group` is what carries the name the field's label
        // gives it. Without it the choices are announced one by one with nothing saying what they belong to.
        var aria = ControlAria();
        if (Label is not null)
        {
            aria["labelledby"] = LabelId;
            aria.Remove("label");
        }

        var selectable = Selectable();
        var allIn = selectable.Count != 0 && selectable.All(picked.Contains);

        return Div
            .Id(FieldId)
            .Role("group")
            .Class(UiClass.Compose(UiChoice.ContainerClass(layout), Class))
            .Aria(aria)[
            CheckAll == true ? CheckAllRow(layout, selectable, picked, allIn, acc, ctx) : null,
            Options.Select((option, index) => Choice(option, index, layout, picked, acc, ctx))
        ];
    }

    private Component CheckAllRow(
        UiChoiceLayout layout,
        List<T> selectable,
        IReadOnlyList<T> picked,
        bool allIn,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx)
    {
        var some = !allIn && selectable.Any(picked.Contains);

        var box = Input
            .Of<bool>()
            .Checked(allIn)
            .Id(FieldId + "-all")
            .Type(InputType.Checkbox)
            .Class(UiClass.Compose(
                "checkbox mt-0.5",
                Tone is { } tone ? UiClassNames.CheckboxTone(tone) : "",
                Size is { } size ? UiClassNames.CheckboxSize(size) : ""))
            .Disabled(Disabled == true)
            // Neither in nor out. The property is the only way to set it — there is no `indeterminate`
            // attribute — so it is `aria-checked="mixed"` that makes it true for assistive tech, and the
            // runtime's own morph leaves the DOM property alone.
            .Aria("checked", some ? "mixed" : allIn ? "true" : "false")
            .OnChange(_ => CommitAsync(allIn ? [] : selectable, acc, ctx));

        return RaskMarkup.Label.Key("--all").Class(UiClass.Compose(
            UiChoice.ChoiceClass(layout == UiChoiceLayout.Cards ? UiChoiceLayout.List : layout),
            layout == UiChoiceLayout.Cards ? "sm:col-span-2" : ""))[
            box,
            Span.Class("text-sm font-medium")[
                CheckAllLabel ?? (allIn ? "Clear all" : "Select all")
            ]
        ];
    }

    private Component Choice(
        (T Value, string Text) option,
        int index,
        UiChoiceLayout layout,
        IReadOnlyList<T> picked,
        ExpressionAccessor.Accessor? acc,
        EditContext? ctx)
    {
        var off = Disabled == true || OptionDisabled?.Invoke(option.Value) == true;
        var isIn = picked.Contains(option.Value);
        var description = layout == UiChoiceLayout.Cards ? OptionDescription?.Invoke(option.Value) : null;

        var box = Input
            .Of<bool>()
            .Checked(isIn)
            .Id(FieldId + "-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Type(InputType.Checkbox)
            .Disabled(off)
            .Class(UiChoice.ShowsBox(layout)
                ? UiClass.Compose(
                    "checkbox mt-0.5",
                    Tone is { } tone ? UiClassNames.CheckboxTone(tone) : "",
                    Size is { } size ? UiClassNames.CheckboxSize(size) : "")
                : UiChoice.HiddenBoxClass)
            .OnChange(_ => Toggle(option.Value, picked, acc, ctx));

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

    private Task Toggle(T value, IReadOnlyList<T> picked, ExpressionAccessor.Accessor? acc, EditContext? ctx)
    {
        var next = new List<T>(picked.Count + 1);
        var removed = false;
        foreach (var item in picked)
        {
            if (EqualityComparer<T>.Default.Equals(item, value))
            {
                removed = true;
                continue;
            }

            next.Add(item);
        }

        if (!removed)
        {
            // In the OPTIONS' order, not the order they were clicked in: a bound collection that reorders
            // itself as somebody picks is a diff nobody asked for, and a form that posts a different order
            // each time.
            next = [.. Options.Select(o => o.Value).Where(v => next.Contains(v) || Same(v, value))];
        }

        return CommitAsync(next, acc, ctx);
    }

    private Task CommitAsync(IReadOnlyList<T> picked, ExpressionAccessor.Accessor? acc, EditContext? ctx) =>
        UiFormCommit.CommitSelectionAsync(this, acc, ctx, picked);

    private List<T> Selectable() =>
        [.. Options.Select(o => o.Value).Where(v => OptionDisabled?.Invoke(v) != true)];

    private IReadOnlyList<T> Current()
    {
        var from = Bind is { } bind ? ExpressionAccessor.Parse(bind).Getter() as ICollection<T> : Value;
        return from is null ? [] : [.. from];
    }

    private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
}
