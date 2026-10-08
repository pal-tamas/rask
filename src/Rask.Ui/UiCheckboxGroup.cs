using System.Linq.Expressions;
using Rask.Core.Components;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
/// Flux UI's checkbox group: a set of choices where any number may be ticked, bound as ONE field.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ui.CheckboxGroup.Bind(() =&gt; model.Topics).Label("Email me about")[Ui.Checkbox.Value("news").Label("News"), …]</c>
/// binds the collection your model declares — a <c>List&lt;T&gt;</c>, a <c>HashSet&lt;T&gt;</c>, an array.
/// The checkboxes are its children; each one's <c>Value</c> is what the collection holds while it is ticked.
/// The controlled opening is <c>Values</c>.
/// </para>
/// <para>
/// A <see cref="UiCheckboxAll" /> among them ticks or clears every one. <see cref="Variant" /> draws the same
/// checkboxes as cards, pills or buttons; each still holds a real <c>&lt;input type="checkbox"&gt;</c>.
/// </para>
/// </remarks>
public sealed partial class UiCheckboxGroup<T> : Component, IFormControl<ICollection<T>>, IUiFormControl
{
    private string? _ownId;

    /// <summary>The group's heading, drawn over it in a <see cref="UiField" />.</summary>
    public string? Label { get; set; }

    /// <summary>Help text between the heading and the checkboxes.</summary>
    public string? Description { get; set; }

    /// <summary>One per row, or cards, pills or buttons.</summary>
    public Ui.CheckboxGroupVariant? Variant { get; set; }

    /// <summary>Disables every checkbox in the group.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Draws every checkbox invalid. A bound group is invalid on its own while its form holds a message for it.</summary>
    public bool? Invalid { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    /// <inheritdoc cref="IUiFormControl.ShowValidation" />
    public bool? ShowValidation { get; set; }

    /// <summary>Classes for the group: <c>flex-col</c> stacks cards, <c>max-sm:flex-col</c> only on a phone.</summary>
    public string? Class { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Value" />
    public ICollection<T>? Value { get; set; }

    /// <inheritdoc cref="IFormControl{T}.OnChange" />
    public Callback<ICollection<T>> OnChange { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Bind" />
    public Expression<Func<ICollection<T>>>? Bind { get; set; }

    /// <inheritdoc cref="IFormControl{T}.Validate" />
    public Validator<ICollection<T>>? Validate { get; set; }

    /// <inheritdoc cref="IFormControl{T}.AfterBind" />
    public Callback<ICollection<T>> AfterBind { get; set; }

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
        List<T> picked = current is null ? [] : [.. current];
        var field = UiWithField.For(this);
        var variant = Variant ?? Ui.CheckboxGroupVariant.Default;

        // The choices as written, and the ones a check-all may change.
        List<T> choices = [];
        List<T> open = [];
        foreach (var member in UiCheckboxScope.Members(Children))
        {
            if (member.Value is T value)
            {
                choices.Add(value);
                if (Disabled != true && member.Disabled != true)
                {
                    open.Add(value);
                }
            }
        }

        var ticked = open.Count(picked.Contains);
        var scope = new UiCheckboxScope(
            field.ControlId,
            variant,
            Disabled == true,
            field.Invalid,
            value => value is T typed && picked.Contains(typed),
            (value, on) => value is T typed
                ? Commit(Selected(picked, choices, [typed], on), accessor, context)
                : Task.CompletedTask,
            open.Count != 0 && ticked == open.Count,
            ticked != 0 && ticked != open.Count,
            on => Commit(Selected(picked, choices, open, on), accessor, context));

        // A plain `group`: ARIA has no "checkboxgroup", and `group` is what carries the heading's name.
        var aria = new Dictionary<string, string?>(field.Aria, StringComparer.Ordinal);
        if (Label is not null)
        {
            aria["labelledby"] = field.LabelId;
        }

        return field.Wrap(
            Div.Id(field.ControlId)
                .Role("group")
                .Class(UiClass.Compose(Look(variant), Class))
                .Aria(aria)
                .Data(Marker(variant), "")[
                Context.Provide(scope)[Children ?? []]
            ]);
    }

    private Task Commit(List<T> next, ExpressionAccessor.Accessor? accessor, EditContext? context) =>
        UiFormCommit.CommitSelectionAsync(this, accessor, context, next);

    // In the default list a row keeps 12px from the next, 16px when it carries a description.
    private static string Look(Ui.CheckboxGroupVariant variant) => variant switch
    {
        Ui.CheckboxGroupVariant.Cards => "flex gap-3",
        Ui.CheckboxGroupVariant.Pills => "flex flex-wrap gap-3",
        Ui.CheckboxGroupVariant.Buttons => "flex gap-2",
        _ => "[&>[data-ui-field]:not(:last-child)]:mb-3 "
             + "[&>[data-ui-field]:has(>[data-ui-description]):not(:last-child)]:mb-4",
    };

    private static string Marker(Ui.CheckboxGroupVariant variant) => variant switch
    {
        Ui.CheckboxGroupVariant.Cards => "ui-checkbox-group-cards",
        Ui.CheckboxGroupVariant.Pills => "ui-checkbox-group-pills",
        Ui.CheckboxGroupVariant.Buttons => "ui-checkbox-group-buttons",
        _ => "ui-checkbox-group",
    };

    // The selection after a change, in the order the choices are written — never the order they were
    // clicked in, or a bound collection would reorder itself as somebody picks.
    private static List<T> Selected(List<T> picked, List<T> choices, List<T> changed, bool on)
    {
        var next = picked.Where(value => !changed.Contains(value)).ToList();
        if (on)
        {
            next.AddRange(changed);
        }

        return [.. next.OrderBy(value => choices.IndexOf(value) is var at && at < 0 ? int.MaxValue : at)];
    }
}
