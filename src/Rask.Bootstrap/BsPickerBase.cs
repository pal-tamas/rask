using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Html.Components;

namespace Rask.Bootstrap;

// Shared base for the custom-popover date/time pickers (BsDatePicker/BsTimePicker/BsDateTimePicker).
// Extends BsFormControl<T> so each picker inherits the full bound+controlled IFormControl<T> surface
// (Bind/Value/OnChange/Validate/AfterBind/Label/…) and reuses Resolve()/ControlId()/SizeClass()/Field().
// This base adds the pieces every picker shares: the open/close view state, the ISO-typed writeback
// that round-trips a boxed value through the accessor (nullable-agnostic), the .form-control trigger
// box + click-outside backdrop + Esc handling (mirroring BsMultiSelect, zero bootstrap.js), and the
// Native opt-out that degrades to a native <input> by delegating to BsInput<T>.

/// <summary>
///     The shared surface behind the date and time pickers — the native/custom switch, the placeholder, and
///     the translatable labels.
/// </summary>
public abstract partial class BsPickerBase<T> : BsFormControl<T>
{
    // Opt out of the custom popover and render the native <input type=date|time|datetime-local> instead
    // (BsInput derives the type from T). Guarantees a working control where the custom UI is unwanted.

    /// <summary>
    ///     Renders the platform's own picker instead of the custom one. Usually the better choice on
    ///     mobile, and the accessible fallback.
    /// </summary>
    public bool? Native { get; set; }

    // Placeholder shown in the trigger box when there is no value.

    /// <summary>The text shown while nothing is chosen.</summary>
    public string? Placeholder { get; set; }

    // Localizable accessible names for the picker chrome (month-nav buttons, time-column headings, clear
    // button) that has no CultureInfo source. Null → English defaults. See BsPickerLabels.

    /// <summary>The picker's user-visible strings, for translation.</summary>
    public BsPickerLabels? Labels { get; set; }

    // The labels to render with — the caller's overrides, or the shared English default.
    private protected BsPickerLabels PickerLabels => Labels ?? BsPickerLabels.Default;

    // Popover visibility — pure live-diff view state, opened on focus, closed by the backdrop or Escape.
    // The value itself lives in the bound model / controlled Value, never here.
    private protected bool Open;

    // The text the user is currently typing into the box (null when not editing → the box shows the value's
    // canonical formatted string). Holds partial/invalid input so a live-committed model can't revert it
    // mid-keystroke; cleared on blur and on every popover pick so the display re-syncs to the value.
    private protected new string? Text;

    // A nullable T (DateOnly?/TimeOnly?/DateTime?) gets a clear affordance; a non-nullable one never does.
    // Computed fresh from typeof(T) on each read rather than cached in a `static readonly` field: under the
    // Mono WASM AOT build a generic base's cached static initializer could resolve typeof(T) against the
    // wrong instantiation (it surfaced as a DateTimeOffset boxed into a DateTime property — see Underlying),
    // and a property re-resolves T in the correct runtime generic context every time.
    private protected static bool CanClear => Nullable.GetUnderlyingType(typeof(T)) is not null;

    // A per-instance suffix so two id-less pickers (controlled mode, no Id, no bound property name) still
    // emit unique grid/cell ids — otherwise their aria-controls/aria-activedescendant would collide. Stable
    // across renders (the instance is preserved by reconciliation).
    private static int _instanceSeq;
    private readonly int _instanceId = System.Threading.Interlocked.Increment(ref _instanceSeq);

    // A per-kind, per-instance fallback id prefix, used when the control has no id/bound-property name.
    private protected string FallbackPrefix(string kind) =>
        $"{kind}{_instanceId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    // The underlying (non-nullable) value type — DateOnly, TimeOnly, DateTime or DateTimeOffset. A property
    // (not a `static readonly` field) so typeof(T) is resolved fresh: see the CanClear note — a cached
    // static field mis-resolved under Mono WASM AOT and made BsDateTimePicker<DateTime> take the
    // DateTimeOffset box branch, throwing "DateTimeOffset cannot be converted to DateTime" on write.
    private protected static Type Underlying => Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

    // The non-nullable type actually targeted by a write: the bound property's real type (reflection —
    // always correct) when bound, else typeof(T) for the controlled callback. Preferring the property type
    // means the composed value is boxed to match the model even if a cached generic lookup were ever wrong.
    private protected static Type TargetUnderlying(ExpressionAccessor.Accessor? acc) =>
        acc is not null
            ? Nullable.GetUnderlyingType(acc.PropertyType) ?? acc.PropertyType
            : Underlying;

    // Display/parse culture: month and weekday names, and first-day-of-week. The bound value still
    // round-trips invariant ISO, because a picker writes the typed value and never a formatted string.
    //
    // Inherited from Component now rather than declared here. That is the whole point of the change:
    // this used to read CultureInfo.CurrentCulture, which on a server is the process locale — so every
    // visitor of a multi-user app got the SERVER's month names. Component.Culture is the session's, and
    // reading it also marks the picker as culture-dependent so a language switch repaints it.

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

        var control = Input
            .Value(BindingHelpers.FormatValue(b.Current))
            .Type(type)
            .Name(Name ?? b.Accessor?.PropertyName)
            .Placeholder(placeholder)
            .Disabled(Disabled)
            .Required(Required)
            .Class(cls)
            .Id(controlId)
            .OnInputAsync(Disabled == true ? null : StringChangeHandler(b));

        return Field(controlId, b, control);
    }

    // Writes a boxed value back to the model (bound) or notifies the parent (controlled). A boxed struct
    // sets both a T and a T? property (PropertyInfo.SetValue converts); null clears a nullable T. The value
    // is coerced to the exact target type first, so a DateTime/DateTimeOffset composed as the wrong one can
    // never reach PropertyInfo.SetValue or the (T) cast and throw "X cannot be converted to type Y".
    private protected async Task WriteBoxedAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, object? boxed)
    {
        if (acc is not null)
        {
            var value = Coerce(acc.PropertyType, boxed);
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid).ConfigureAwait(false);
            // Fire AfterBind on every write, including a clear (boxed == null → default(T), which is null
            // for the nullable T that owns a clear button) — matches the standard bound Setter(null) path.
            await ((IFormControl<T>)this).InvokeAfterBindAsync(value is null ? default! : (T)value)
                .ConfigureAwait(false);
        }
        else
        {
            var value = Coerce(typeof(T), boxed);
            var typed = value is null ? default! : (T)value;
            await ((IFormControl<T>)this).InvokeOnChangeAsync(typed).ConfigureAwait(false);
        }
    }

    // Coerces a composed date/time value to the exact type the write target expects, decided by the value's
    // RUNTIME type (via IsInstanceOfType — never a typeof() comparison, which can mis-fire under some
    // runtimes). The only legitimate mismatch is DateTime <-> DateTimeOffset; anything already assignable is
    // returned untouched, so a matching value (with its preserved offset) is never disturbed.
    private static object? Coerce(Type targetType, object? boxed)
    {
        if (boxed is null || targetType.IsInstanceOfType(boxed))
        {
            return boxed;
        }

        return boxed switch
        {
            DateTime dt => new DateTimeOffset(
                DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(dt)),
            DateTimeOffset dto => dto.DateTime,
            _ => boxed,
        };
    }

    // Assembles the .dropdown shell: an editable .form-control combobox INPUT (type the value; focus opens
    // the popover), a caret / clear button, the popover (built by the picker), the backdrop, and the
    // label/help/invalid-feedback — built here (not via Field) so floating wraps just the box + label in a
    // .form-floating.bs-floating (Field would wrap the whole .dropdown, which breaks Bootstrap's selector).
    private protected Component RenderShell(
        in Bound b,
        string? controlId,
        string gridId,
        string formatted,
        IReadOnlyDictionary<string, string?> boxAria,
        Func<string, Task> onParse,
        Func<KeyboardEventArgs, Task> onKeyDown,
        bool hasValue,
        Component? popover,
        Func<Task> clearAsync)
    {
        var disabled = Disabled == true;
        var showClear = CanClear && hasValue && !disabled;
        var floating = Floating is true && Label is not null;

        var box = Input
            .Value(Text ?? formatted)
            .Type(InputType.Text)
            .Class(BsClass.Join("form-control", SizeClass("form-control"), b.Invalid ? "is-invalid" : null))
            .Id(controlId)
            .Placeholder(floating ? null : Placeholder)
            .Disabled(Disabled)
            .Autocomplete("off")
            .Data(BsPopover.Anchor)
            .Role("combobox")
            .Aria(boxAria)
            .OnFocus(disabled ? null : () => Open = true)
            .OnClick(disabled ? null : () => Open = true)
            .OnBlur(disabled ? null : () => Text = null)
            .OnInputAsync(disabled ? null : raw => { Text = raw; return onParse(raw); })
            .OnKeyDownAsync(disabled ? null : onKeyDown);

        var caret = showClear
            ? null
            : Span
                .Class(BsClass.Join(Position.Absolute, Position.End0, Position.Top50,
                    Position.TranslateMiddleY, Margin.End(3), "bs-picker-caret"))
                .Aria(Hidden)["▾"];

        Component? clear = showClear
            ? BsCloseButton
                .Class(BsClass.Join(Position.Absolute, Position.End0, Position.Top50,
                    Position.TranslateMiddleY, Margin.End(2), "bs-picker-clear", Open ? "bs-clear-open" : null))
                .AriaLabel(PickerLabels.Clear)
                .OnClickAsync(clearAsync)
            : null;

        Component? backdrop = Open && !disabled
            ? Div
                .Class(BsClass.Join(Position.Fixed, Position.Top0, Position.Start0,
                    Sizing.W(100), Sizing.H(100)))
                .Style("z-index: 999;")
                .OnClick(() => { Open = false; Text = null; })
            : null;

        var labelNode = Label is null
            ? null
            : global::RaskEntriesRask_Html.Label.For(controlId).Class(floating ? null : "form-label")[
                Label,
                Required is true ? Span.Class("text-danger ms-1")["*"] : null];

        var children = new List<Component?>();
        if (labelNode is not null && !floating)
        {
            children.Add(labelNode);
        }

        // Floating wraps box + label (+ the absolutely-placed caret/×) in a position-relative .form-floating;
        // the popover/backdrop stay direct children of the .dropdown. Non-floating: box + caret/× go in their
        // OWN position-relative wrapper so the caret/× centre on the box alone — anchoring them to the whole
        // .dropdown would centre them over the label-above + box stack, dropping them onto the box's top edge.
        if (floating)
        {
            children.Add(Div
                .Class(BsClass.Join("form-floating bs-floating", hasValue ? "bs-floating-filled" : null,
                    Position.Relative))[box, labelNode, caret, clear]);
        }
        else
        {
            children.Add(Div.Class(Position.Relative)[box, caret, clear]);
        }

        // The popover is always in the DOM (like BsMultiSelect's menu); the picker toggles .show/.d-block
        // from Open, so a closed picker still renders the grid (hidden) and its markup is testable.
        children.Add(popover);
        children.Add(backdrop);

        if (HelpText is not null)
        {
            children.Add(Div.Id(HelpTextId(controlId)).Class("form-text")[HelpText]);
        }

        if (b.Invalid)
        {
            children.Add(Div.Id(ErrorId(controlId, b)).Class("invalid-feedback d-block").Role("alert")[
                b.Messages[0]]);
        }

        return Div.Class(BsClass.Join("dropdown", Position.Relative, Class)).Data(BsPopover.Wrapper)[children];
    }

    private protected static readonly IReadOnlyDictionary<string, string?> Hidden =
        new Dictionary<string, string?> { ["hidden"] = "true" };

    // Shared grid keyboard navigation, used by BsDatePicker and BsDateTimePicker (the box keeps DOM focus;
    // aria-activedescendant tracks the cursor). Returns the moved cursor for a grid-movement key (the caller
    // clamps to its own range), or null when the key isn't a grid move. Arrows move a day/week, PageUp/Down a
    // month (Shift a year), Home/End the culture week edge. Left/Right are deliberately NOT contained in the
    // client (rask-dom.js) so the editable box keeps its text caret; the day cursor moving alongside is benign.
    private protected DateOnly? GridMove(DateOnly cursor, KeyboardEventArgs e) => e.Key switch
    {
        "ArrowLeft" => cursor.AddDays(-1),
        "ArrowRight" => cursor.AddDays(1),
        "ArrowUp" => cursor.AddDays(-7),
        "ArrowDown" => cursor.AddDays(7),
        "PageUp" => e.Shift ? cursor.AddYears(-1) : cursor.AddMonths(-1),
        "PageDown" => e.Shift ? cursor.AddYears(1) : cursor.AddMonths(1),
        "Home" => PickerParts.WeekStart(cursor, Culture),
        "End" => PickerParts.WeekEnd(cursor, Culture),
        _ => null,
    };

    // A closed, focused grid picker opens on a navigation key. Enter and Space are intentionally excluded:
    // Enter in a form-bound text box is the form's submit key (and reopening while submitting is contradictory),
    // and Space is a literal text character the client can't suppress without breaking typing.
    private protected static bool IsGridOpenKey(KeyboardEventArgs e) =>
        e.Key is "ArrowDown" or "ArrowUp" or "ArrowLeft" or "ArrowRight"
            or "PageUp" or "PageDown" or "Home" or "End";

    // The popover wrapper class — always a .dropdown-menu, gaining .show + d-block only while Open.
    private protected string? MenuClass() =>
        BsClass.Join("dropdown-menu", Open ? "show" : null, Open ? Display.Block() : null, "p-2");
}
