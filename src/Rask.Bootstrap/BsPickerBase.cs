using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// Shared base for the custom-popover date/time pickers (BsDatePicker/BsTimePicker/BsDateTimePicker).
// Extends BsFormControl<T> so each picker inherits the full bound+controlled IFormControl<T> surface
// (Bind/Value/OnChange/Validate/AfterBind/Label/…) and reuses Resolve()/ControlId()/SizeClass()/Field().
// This base adds the pieces every picker shares: the open/close view state, the ISO-typed writeback
// that round-trips a boxed value through the accessor (nullable-agnostic), the .form-control trigger
// box + click-outside backdrop + Esc handling (mirroring BsMultiSelect, zero bootstrap.js), and the
// Native opt-out that degrades to a native <input> by delegating to BsInput<T>.
public abstract class BsPickerBase<T> : BsFormControl<T>
{
    // Opt out of the custom popover and render the native <input type=date|time|datetime-local> instead
    // (BsInput derives the type from T). Guarantees a working control where the custom UI is unwanted.
    public bool? Native { get; set; }

    // Placeholder shown in the trigger box when there is no value.
    public string? Placeholder { get; set; }

    // Popover visibility — pure live-diff view state, toggled by the box click / keyboard, closed by the
    // backdrop or Escape. The value itself lives in the bound model / controlled Value, never here.
    private protected bool Open;

    // A nullable T (DateOnly?/TimeOnly?/DateTime?) gets a clear affordance; a non-nullable one never does.
    private protected static readonly bool CanClear = Nullable.GetUnderlyingType(typeof(T)) is not null;

    // A per-instance suffix so two id-less pickers (controlled mode, no Id, no bound property name) still
    // emit unique grid/cell ids — otherwise their aria-controls/aria-activedescendant would collide. Stable
    // across renders (the instance is preserved by reconciliation).
    private static int _instanceSeq;
    private readonly int _instanceId = System.Threading.Interlocked.Increment(ref _instanceSeq);

    // A per-kind, per-instance fallback id prefix, used when the control has no id/bound-property name.
    private protected string FallbackPrefix(string kind) =>
        $"{kind}{_instanceId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    // The underlying (non-nullable) value type — DateOnly, TimeOnly, DateTime or DateTimeOffset.
    private protected static readonly Type Underlying = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

    // Display/parse culture. CurrentCulture drives month/weekday names and first-day-of-week; the bound
    // value still round-trips invariant ISO because we write the typed value, never a formatted string.
    private protected static System.Globalization.CultureInfo Culture =>
        System.Globalization.CultureInfo.CurrentCulture;

    // The native-input fallback for Native:true — the SAME core <input> BsInput renders (a
    // type=date|time|datetime-local bound to a string round-tripped by FormatValue/StringChangeHandler),
    // built through the base helpers rather than instantiating BsInput (RASK014) or fighting the bound
    // factory's validator fan-out. Validators ride along via Resolve()'s RegisterValidator + the
    // StringChangeHandler, exactly as in the custom path.
    private protected Component NativeInput(InputType type)
    {
        var b = Resolve();
        var controlId = ControlId(b);
        var cls = BsClass.Join("form-control", SizeClass("form-control"), b.Invalid ? "is-invalid" : null, Class);
        var placeholder = Floating is true ? Placeholder ?? Label ?? " " : Placeholder;

        var control = Input<string>(
            Type: type,
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Placeholder: placeholder, Disabled: Disabled, Required: Required,
            Class: cls, Id: controlId,
            OnInputAsync: Disabled == true ? null : StringChangeHandler(b));

        return Field(controlId, b, control);
    }

    // Writes a boxed value back to the model (bound) or notifies the parent (controlled). A boxed struct
    // sets both a T and a T? property (PropertyInfo.SetValue converts); null clears a nullable T.
    private protected async Task WriteBoxedAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, object? boxed)
    {
        if (acc is not null)
        {
            acc.Setter(boxed);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            // Fire AfterBind on every write, including a clear (boxed == null → default(T), which is null
            // for the nullable T that owns a clear button) — matches the standard bound Setter(null) path.
            await ((IFormControl<T>)this).InvokeAfterBindAsync(boxed is null ? default! : (T)boxed)
                .ConfigureAwait(false);
        }
        else
        {
            var typed = boxed is null ? default! : (T)boxed;
            await ((IFormControl<T>)this).InvokeOnChangeAsync(typed).ConfigureAwait(false);
        }
    }

    // Assembles the .dropdown shell: the .form-control combobox trigger (label-linked, focusable, with a
    // caret or a clear button), the popover (already built by the picker), and the full-screen backdrop.
    // Wrapped by Field() so the label + help-text + .invalid-feedback come out identical to BsInput.
    private protected Component RenderShell(
        in Bound b,
        string? controlId,
        string gridId,
        Component valueContent,
        IReadOnlyDictionary<string, string?> boxAria,
        Callback onToggle,
        CallbackAsync<KeyboardEventArgs> onKeyDown,
        bool hasValue,
        Component? popover,
        CallbackAsync clearAsync)
    {
        var disabled = Disabled == true;
        var showClear = CanClear && hasValue && !disabled;

        var box = Div(
            Class: BsClass.Join("form-control", SizeClass("form-control"),
                Display.Flex(), Flex.Align(BsAlign.Center),
                b.Invalid ? "is-invalid" : null, disabled ? "disabled pe-none" : null, Class),
            Id: controlId,
            Role: "combobox",
            TabIndex: disabled ? null : 0,
            Aria: boxAria,
            OnClick: disabled ? null : onToggle,
            OnKeyDownAsync: disabled ? null : onKeyDown)[
            valueContent,
            showClear
                ? null
                : Span(Class: BsClass.Join(Margin.StartAuto, "ps-2", "bs-picker-caret"), Aria: Hidden)["▾"]
        ];

        var clear = showClear
            ? BsCloseButton(
                Class: BsClass.Join(Position.Absolute, Position.End0, Position.Top50,
                    Position.TranslateMiddleY, Margin.End(2)),
                AriaLabel: "Clear",
                OnClickAsync: clearAsync)
            : null;

        var backdrop = Open && !disabled
            ? Div(
                Class: BsClass.Join(Position.Fixed, Position.Top0, Position.Start0,
                    Sizing.W(100), Sizing.H(100)),
                Style: "z-index: 999;",
                OnClick: () => Open = false)
            : null;

        // The popover is always in the DOM (like BsMultiSelect's menu); the picker toggles .show/.d-block
        // from Open, so a closed picker still renders the grid (hidden) and its markup is testable.
        var control = Div(Class: BsClass.Join("dropdown", Position.Relative))[
            box,
            clear,
            popover,
            backdrop
        ];

        return Field(controlId, b, control);
    }

    private protected static readonly IReadOnlyDictionary<string, string?> Hidden =
        new Dictionary<string, string?> { ["hidden"] = "true" };

    // The popover wrapper class — always a .dropdown-menu, gaining .show + d-block only while Open.
    private protected string? MenuClass() =>
        BsClass.Join("dropdown-menu", Open ? "show" : null, Open ? Display.Block() : null, "p-2");
}
