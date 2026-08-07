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
    public new string? Label { get; set; }
    public bool? Disabled { get; set; }
    public bool? Required { get; set; }
    public BsSize? Size { get; set; }

    // Muted helper text rendered under the control (.form-text).
    public string? HelpText { get; set; }
    public string? Name { get; set; }

    // Field() bakes the per-field .invalid-feedback straight into this control's own render output from
    // the EditContext's mutable message list. No manual BypassRenderCache: Resolve() reads
    // EditContext.GetValidationMessages during Render(), which auto-latches the render-cache opt-out
    // (see EditContext.MarkReader / Component._readsAmbientState), so validation produced during the submit
    // pipeline always repaints instead of being served stale from a pre-submit cache.

    // Bootstrap floating label (https://getbootstrap.com/docs/5.0/forms/floating-labels/): wraps the
    // control + label in a .form-floating with the label AFTER the control, so the label floats over an
    // empty field and shrinks on focus/fill. Requires a Label; controls that need a placeholder for the
    // effect (BsInput/BsTextarea) supply one from the Label when floating.
    public bool? Floating { get; set; }

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

    // Stable ids for the help text and error feedback, derived from the control id so the control's
    // aria-describedby can point at them. Null when there is no control id to anchor to (a purely
    // controlled control with no Id/Name/PropertyName) or the target markup isn't rendered.
    private protected string? HelpTextId(string? controlId) =>
        controlId is not null && HelpText is not null ? controlId + "-help" : null;

    private protected static string? ErrorId(string? controlId, in Bound b) =>
        controlId is not null && b.Invalid ? controlId + "-error" : null;

    // The aria-* attributes for the control: aria-invalid when the field has validation messages, and
    // aria-describedby wiring the control to its help text and/or its error feedback so assistive tech
    // announces both alongside the field. Built via the shared BsClass.FieldAria so all four Bs form
    // controls emit the same aria-* contract; returned as the framework Aria dictionary (keys without
    // the "aria-" prefix), so the serializer emits them in the canonical aria-* attribute slot.
    private protected IReadOnlyDictionary<string, string?>? FieldAria(in Bound b, string? controlId)
    {
        var helpId = HelpTextId(controlId);
        var errorId = ErrorId(controlId, b);
        var describedBy = helpId is not null
            ? errorId is not null ? helpId + " " + errorId : helpId
            : errorId;
        return BsClass.FieldAria(b.Invalid, describedBy);
    }

    private protected string? SizeClass(string prefix) =>
        Size is { } s && s.Suffix() is { } suffix ? $"{prefix}-{suffix}" : null;

    // Wraps a control element with an optional label + help-text/invalid-feedback. Default puts the
    // label above; Floating wraps control+label in a .form-floating (label after the control). The
    // whole field is boxed in a single wrapper <div> so it is ONE layout item: in a flex/grid form
    // (e.g. .d-flex.flex-column.gap-3) the .invalid-feedback then sits tight under its input instead
    // of becoming a separate gap-spaced sibling one row below.
    private protected Component Field(string? controlId, in Bound b, Component control)
    {
        // Help text and error feedback carry the ids the control's aria-describedby points at, and the
        // error container is a role="alert" live region so a screen reader announces the message the
        // moment validation fails (on submit/blur), associated with — not detached from — the field.
        var help = HelpText is not null ? Div(Id: HelpTextId(controlId), Class: "form-text")[HelpText] : null;
        var feedback = b.Invalid
            ? Div(Id: ErrorId(controlId, b), Class: "invalid-feedback d-block", Role: "alert")[b.Messages[0]]
            : null;

        if (Floating is true && Label is not null)
        {
            return Div()[
                Div(Class: "form-floating")[
                    control,
                    RequiredLabel(controlId, null)
                ],
                help,
                feedback
            ];
        }

        return Div()[
            Label is not null ? RequiredLabel(controlId, "form-label") : null,
            control,
            help,
            feedback
        ];
    }

    // The field <label>, with a red asterisk appended when the control is Required so every required
    // field is marked consistently without each call site repeating the markup. Absent Required, the
    // asterisk span is null and the label renders exactly as before.
    private Component RequiredLabel(string? controlId, string? cls) =>
        Rask.Core.Components.Generated.Label(For: controlId, Class: cls)[
            Label,
            Required is true ? Span(Class: "text-danger ms-1")["*"] : null
        ];
}
