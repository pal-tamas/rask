using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Example.Shared;

// Example generic form control: the single-value sibling of CheckboxGroup — a set of mutually-exclusive
// Bootstrap 5.3 radios (https://getbootstrap.com/docs/5.3/forms/checks-radios/) bound to one TValue.
// Structured like MultiSelect/CheckboxGroup, with two shapes:
//   • Bound      — RadioGroup(() => model.Plan, options, Validate: …) two-way binds the scalar and runs the
//                  per-field Validate rule (surfaced via the embedded ValidationMessage).
//   • Controlled — RadioGroup(options, Value: current, OnChange: v => …) the parent owns the value; OnChange/
//                  OnChangeAsync (auto-wrapped) deliver the new value and re-render the host. No Validate.
// Mode is chosen by whether Bind is set (a value type can't use a null Value as the signal). Each item is a
// <div class="form-check"> with a .form-check-input radio + .form-check-label tied by id/for; ItemClass adds
// extra wrapper classes (e.g. "form-check-inline").
public sealed class RadioGroup<TValue> : Component
{
    public required IEnumerable<TValue> Options { get; set; }

    // Controlled mode (used when Bind is null): the parent owns the current value.
    public TValue? Value { get; set; }
    public Action<TValue>? OnChange { get; set; }
    public Func<TValue, Task>? OnChangeAsync { get; set; }

    // Bound mode — set through the Bind-first factory overloads, kept off the controlled factory.
    [SkipFactory] public Expression<Func<TValue>>? Bind { get; set; }
    [SkipFactory] public Action<TValue>? AfterBind { get; set; }
    [SkipFactory] public Func<TValue, Task>? AfterBindAsync { get; set; }
    [SkipFactory] public Delegate? Validate { get; set; }

    public Func<TValue, Child>? OptionLabel { get; set; }
    public string? Name { get; set; }
    public string? ItemClass { get; set; }
    public bool? Disabled { get; set; }

    [GenerateForwarderFactory(Validator = "Validate")]
    public static RadioGroup<TValue> Bound(
        Expression<Func<TValue>> Bind,
        IEnumerable<TValue> Options,
        Delegate? Validate = null,
        Action<TValue>? AfterBind = null,
        Func<TValue, Task>? AfterBindAsync = null,
        Func<TValue, Child>? OptionLabel = null,
        string? Name = null,
        string? ItemClass = null,
        bool Disabled = false)
    {
        var c = Generated.RadioGroup<TValue>(
            Options, OptionLabel: OptionLabel, Name: Name, ItemClass: ItemClass, Disabled: Disabled);
        c.Bind = Bind;
        c.AfterBind = AfterBind;
        c.AfterBindAsync = AfterBindAsync;
        c.Validate = Validate;
        return c;
    }

    protected override RenderResult Render()
    {
        ArgumentNullException.ThrowIfNull(Options);

        var bound = Bind is not null;
        if (!bound && OnChange is null && OnChangeAsync is null)
        {
            throw new InvalidOperationException(
                "RadioGroup requires Bind (bound mode) or an OnChange/OnChangeAsync handler (controlled mode).");
        }

        var comparer = EqualityComparer<TValue>.Default;
        ExpressionAccessor.Accessor? acc = null;
        EditContext? ctx = null;
        var fid = default(FieldIdentifier);
        TValue? current;
        if (bound)
        {
            acc = ExpressionAccessor.Parse(Bind!);
            ctx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            ctx?.RegisterFieldValidator(fid, Validate, () => acc.Getter());
            current = acc.Getter() is TValue typed ? typed : default;
        }
        else
        {
            current = Value;
        }

        var disabled = Disabled == true;
        var groupName = Name ?? acc?.PropertyName ?? "radio-group";
        var wrapperClass = ItemClass is null ? "form-check" : $"form-check {ItemClass}";

        var children = new List<Child>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option;
            var optionId = $"{groupName}-{index}";
            var isChecked = current is not null && comparer.Equals(optionValue, current);
            Child label = OptionLabel is not null ? OptionLabel(option) : option?.ToString() ?? string.Empty;

            children.Add(Div(Class: wrapperClass, Key: index)[
                Input(
                    "radio",
                    groupName,
                    BindingHelpers.FormatValue(option),
                    Checked: isChecked,
                    Disabled: Disabled,
                    Class: "form-check-input",
                    Id: optionId,
                    // A radio only fires change when it becomes selected → set the value.
                    OnChangeAsync: disabled ? null : _ => SelectAsync(acc, ctx, fid, optionValue)),
                Label(Class: "form-check-label", For: optionId)[label]
            ]);
            index++;
        }

        if (bound)
        {
            children.Add(ValidationMessage(Bind!, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]]));
        }

        return Fragment()[children];
    }

    private async Task SelectAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TValue value)
    {
        if (acc is not null)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            AfterBind?.Invoke(value);
            if (AfterBindAsync is not null)
            {
                await AfterBindAsync(value).ConfigureAwait(false);
            }
        }
        else
        {
            OnChange?.Invoke(value);
            if (OnChangeAsync is not null)
            {
                await OnChangeAsync(value).ConfigureAwait(false);
            }
        }
    }
}
