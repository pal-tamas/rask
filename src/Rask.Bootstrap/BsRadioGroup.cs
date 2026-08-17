using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A set of mutually-exclusive Bootstrap radios bound to one TValue. Implements IFormControl<TValue> so
// the generator emits the bound factory (BsRadioGroup(() => model.Plan, options, Validate: …)) and the
// controlled factory (Value:/OnChange:). Each item is a <div class="form-check"> with a .form-check-input
// radio + .form-check-label; the embedded ValidationMessage surfaces the per-field rule. Mode is chosen
// by whether Bind is set.
public sealed partial class BsRadioGroup<TValue> : Component, IFormControl<TValue>
{
    public required IEnumerable<TValue> Options { get; set; }

    // Controlled mode (used when Bind is null): the parent owns the current value.
    public TValue? Value { get; set; }
    public Action<TValue>? OnChange { get; set; }
    public Func<TValue, Task>? OnChangeAsync { get; set; }

    // Bound mode (IFormControl members).
    public Expression<Func<TValue>>? Bind { get; set; }
    public Validate<TValue>? Validate { get; set; }
    public ValidateAsync<TValue>? ValidateAsync { get; set; }
    public Action<TValue>? AfterBind { get; set; }
    public Func<TValue, Task>? AfterBindAsync { get; set; }

    public Func<TValue, Component>? OptionLabel { get; set; }
    public string? Name { get; set; }

    // The group's accessible name. When set, the radios are wrapped in a <fieldset> named by a <legend>
    // (the correct grouping semantics + accessible name for a set of related radios); when null, the bare
    // per-item fragment is kept so callers that supply their own fieldset/heading aren't double-wrapped.
    public string? Label { get; set; }

    // Extra wrapper classes per item, e.g. "form-check-inline".
    public string? ItemClass { get; set; }
    public bool? Disabled { get; set; }

    // Unique suffix for the auto-generated group name of an UNNAMED group, so two id-less controlled
    // radio groups on one page don't both fall back to name="radio-group" — which would make the browser
    // treat them as one radio group (selecting in one clears the other) and collide their option ids.
    private readonly int _instanceId = BsInstanceId.Next();

    protected override Component? Render()
    {
        ArgumentNullException.ThrowIfNull(Options);

        var bound = Bind is not null;
        if (!bound && OnChange is null && OnChangeAsync is null)
        {
            throw new InvalidOperationException(
                "BsRadioGroup requires Bind (bound mode) or an OnChange/OnChangeAsync handler (controlled mode).");
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
            ((IFormControl<TValue>)this).RegisterValidator(acc, ctx);
            current = acc.Getter() is TValue typed ? typed : default;
        }
        else
        {
            current = Value;
        }

        var disabled = Disabled == true;
        var groupName = Name ?? acc?.PropertyName
            ?? "radio-group-" + _instanceId.ToString(CultureInfo.InvariantCulture);
        var wrapperClass = BsClass.Join("form-check", ItemClass);

        // Reading GetValidationMessages here latches the render-cache opt-out (see BsFormControl) so the group
        // repaints its aria-invalid + feedback when a rule fails on submit, instead of serving a stale cache.
        // The error is a role="alert" live region carrying a stable id the radios point at via aria-describedby.
        IReadOnlyList<string> messages = bound && ctx is not null ? ctx.GetValidationMessages(fid) : [];
        var invalid = messages.Count > 0;
        var errorId = invalid ? groupName + "-error" : null;
        var optionAria = BsClass.FieldAria(invalid, errorId);

        var children = new List<Component>();
        var index = 0;
        foreach (var option in Options)
        {
            var optionValue = option;
            var optionId = $"{groupName}-{index}";
            var isChecked = current is not null && comparer.Equals(optionValue, current);
            Component label = OptionLabel is { } render ? render(option) : option?.ToString() ?? string.Empty;

            children.Add(Div.Class(wrapperClass).Key(index)[
                Input
                    .Value(BindingHelpers.FormatValue(option))
                    .Type(InputType.Radio)
                    .Name(groupName)
                    .Checked(isChecked)
                    .Disabled(Disabled)
                    .Class("form-check-input")
                    .Id(optionId)
                    .Aria(optionAria)
                    .OnChangeAsync(disabled ? null : _ => SelectAsync(acc, ctx, fid, optionValue)),
                global::RaskEntriesRask_Html.Label.Class("form-check-label").For(optionId)[label]
            ]);
            index++;
        }

        if (invalid)
        {
            children.Add(Div.Id(errorId).Class("invalid-feedback d-block").Role("alert")[messages[0]]);
        }

        if (Label is not null)
        {
            var content = new List<Component> { Legend.Class("form-label fs-6")[Label] };
            content.AddRange(children);
            // Disabled is NOT set on the fieldset: a disabled fieldset disables ALL descendants, which would
            // also disable interactive content in a rich OptionLabel. The radios carry their own Disabled.
            return Fieldset.Class("border-0 p-0 m-0")[content];
        }

        return [.. children];
    }

    private async Task SelectAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TValue value)
    {
        if (acc is not null)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            await ((IFormControl<TValue>)this).InvokeAfterBindAsync(value).ConfigureAwait(false);
        }
        else
        {
            await ((IFormControl<TValue>)this).InvokeOnChangeAsync(value).ConfigureAwait(false);
        }
    }
}
