using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Bootstrap;

// Base for the Bootstrap form controls (BsInput/BsTextarea/BsSelect/BsCheck). Implements
// IFormControl<T> so the generator emits the bound factory (BsInput(() => model.Email, …)) and the
// controlled factory (Value:/OnChange:); the controls themselves WRAP the core Input/Select/Textarea
// and reuse the framework binding helpers (RegisterValidator/StringSetHandler/…) — no re-implemented
// binding. Mirrors the worked-example controls (RadioGroup/CheckboxGroup).
public abstract class BsFormControl<T> : BsBlock, IFormControl<T>
{
    // IFormControl<T> — bound mode.
    public Expression<Func<T>>? Bind { get; set; }
    public Validate<T>? Validate { get; set; }
    public ValidateAsync<T>? ValidateAsync { get; set; }
    public Action<T>? AfterBind { get; set; }
    public Func<T, Task>? AfterBindAsync { get; set; }

    // IFormControl<T> — controlled mode.
    public T? Value { get; set; }
    public Callback<T>? OnChange { get; set; }
    public CallbackAsync<T>? OnChangeAsync { get; set; }

    // Shared Bootstrap field props.
    public string? Label { get; set; }
    public bool? Disabled { get; set; }
    public bool? Required { get; set; }
    public BsSize? Size { get; set; }

    // Muted helper text rendered under the control (.form-text).
    public string? HelpText { get; set; }
    public string? Name { get; set; }

    // The resolved binding for a render: accessor/context/field + current value + validation state.
    private protected readonly record struct Bound(
        bool IsBound,
        ExpressionAccessor.Accessor? Accessor,
        EditContext? Context,
        FieldIdentifier Field,
        T? Current,
        IReadOnlyList<string> Messages)
    {
        public bool Invalid => Messages.Count > 0;
    }

    // Parses Bind (if any), registers the per-field validator, and reads the current value + messages.
    private protected Bound Resolve()
    {
        if (Bind is null)
        {
            return new Bound(false, null, null, default, Value, []);
        }

        var acc = ExpressionAccessor.Parse(Bind);
        var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
        ((IFormControl<T>)this).RegisterValidator(acc, ctx);
        var current = acc.Getter() is T t ? t : default;
        var messages = ctx?.GetValidationMessages(acc.Field) ?? [];
        return new Bound(true, acc, ctx, acc.Field, current, messages);
    }

    // The change handler for a string-valued control: the model writeback (bound) or the typed
    // controlled-change bridge. Reused by BsInput/BsSelect/BsTextarea.
    private protected CallbackAsync<string>? StringChangeHandler(in Bound b) =>
        b is { IsBound: true, Accessor: { } acc }
            ? BindingHelpers.StringSetHandler(acc, b.Context, b.Field, validateOnSet: false,
                afterBind: BindingHelpers.BuildAfterBind(acc, AfterBind, AfterBindAsync))
            : (CallbackAsync<string>?)((IFormControl<T>)this).ControlledChangeHandler();

    // The id used to tie the <label for> to the control.
    private protected string? ControlId(in Bound b) => Id ?? b.Accessor?.PropertyName ?? Name;

    private protected string? SizeClass(string prefix) =>
        Size is { } s && s.Suffix() is { } suffix ? $"{prefix}-{suffix}" : null;

    // Wraps a control element with an optional label above and help-text/invalid-feedback below.
    private protected RenderResult Field(string? controlId, in Bound b, Child control) => Fragment()[
        Label is not null
            ? Rask.Core.Components.Generated.Label(For: controlId, Class: "form-label")[Label]
            : (Child)Fragment(),
        control,
        HelpText is not null ? Div(Class: "form-text")[HelpText] : (Child)Fragment(),
        b.Invalid ? Div(Class: "invalid-feedback d-block")[b.Messages[0]] : (Child)Fragment()];
}
