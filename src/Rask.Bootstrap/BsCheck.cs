using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A Bootstrap checkbox or switch bound to a bool: <div class="form-check"><input
// class="form-check-input" type="checkbox"><label class="form-check-label">. Bound:
// BsCheck(() => model.AcceptTerms, Label: "I accept"). Set Switch for the toggle look.
//
// Implements IFormControl<bool> directly (not via the generic BsFormControl<T> base): an
// unconstrained `T?` collapses to `T` for value types, so closing the generic base at bool would emit
// an invalid `bool Value = null` factory parameter. Declaring the members as explicit bool? avoids it.
public sealed class BsCheck : BsBlock, IFormControl<bool>
{
    // IFormControl<bool> — bound mode.
    public Expression<Func<bool>>? Bind { get; set; }
    public Validate<bool>? Validate { get; set; }
    public ValidateAsync<bool>? ValidateAsync { get; set; }
    public Action<bool>? AfterBind { get; set; }
    public Func<bool, Task>? AfterBindAsync { get; set; }

    // IFormControl<bool> — controlled mode. Value is plain bool (the interface's `T?` resolves to
    // `bool` for the value type T=bool); the generator gives it a `= default` (false) factory default.
    public bool Value { get; set; }
    public Callback<bool>? OnChange { get; set; }
    public CallbackAsync<bool>? OnChangeAsync { get; set; }

    public string? Label { get; set; }
    public bool? Disabled { get; set; }
    public bool? Required { get; set; }
    public string? Name { get; set; }

    // Renders the switch toggle (.form-switch + role="switch").
    public bool? Switch { get; set; }

    // Lays the check inline (.form-check-inline) / right-aligned (.form-check-reverse).
    public bool? Inline { get; set; }
    public bool? Reverse { get; set; }

    protected override RenderResult Render()
    {
        ExpressionAccessor.Accessor? acc = null;
        EditContext? ctx = null;
        var fid = default(FieldIdentifier);
        bool current;
        IReadOnlyList<string> messages = [];

        if (Bind is not null)
        {
            acc = ExpressionAccessor.Parse(Bind);
            ctx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            ((IFormControl<bool>)this).RegisterValidator(acc, ctx);
            current = acc.Getter() is true;
            messages = ctx?.GetValidationMessages(fid) ?? [];
        }
        else
        {
            current = Value;
        }

        var invalid = messages.Count > 0;
        var controlId = Id ?? acc?.PropertyName ?? Name;

        CallbackAsync<string>? change = acc is not null
            ? BindingHelpers.BoolSetHandler(acc, ctx, fid, BindingHelpers.BuildAfterBind(acc, AfterBind, AfterBindAsync))
            : (CallbackAsync<string>?)((IFormControl<bool>)this).ControlledChangeHandler();

        var input = Input<string>(
            Type: InputType.Checkbox,
            Name: Name ?? acc?.PropertyName,
            Checked: current,
            Disabled: Disabled, Required: Required,
            Role: Switch is true ? "switch" : null,
            Class: BsClass.Join("form-check-input", invalid ? "is-invalid" : null),
            Id: controlId,
            OnChangeAsync: Disabled == true ? null : change);

        var wrapperCls = BsClass.Join("form-check",
            Switch is true ? "form-switch" : null,
            Inline is true ? "form-check-inline" : null,
            Reverse is true ? "form-check-reverse" : null,
            Class);

        return Div(Id: Id, Class: wrapperCls)[
            input,
            Label is not null
                ? Rask.Core.Components.Generated.Label(For: controlId, Class: "form-check-label")[Label]
                : (Child)Fragment(),
            invalid ? Div(Class: "invalid-feedback d-block")[messages[0]] : (Child)Fragment()];
    }
}
