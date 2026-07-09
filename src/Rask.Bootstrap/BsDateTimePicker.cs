using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap date+time picker: a .form-control trigger that opens a popover pairing the month-grid
// calendar with the hour/minute lists, bound to a DateTime (or DateTime?/DateTimeOffset/DateTimeOffset?).
// Pure live-diff view state — no bootstrap.js. Picking a day preserves the time-of-day; picking an
// hour/minute preserves the date, so the two halves compose one value. Min/Max/Disable constrain the
// calendar. Keyboard mirrors BsDatePicker for the grid. Native:true falls back to
// <input type=datetime-local>. Reuses PickerParts.CalendarGrid + TimeColumns (no duplicated markup).
//   Bound:      BsDateTimePicker(() => model.When, Label: "When")
//   Controlled: BsDateTimePicker(Value: dt, OnChange: v => …)
public sealed class BsDateTimePicker<T> : BsPickerBase<T>
{
    public DateTime? Min { get; set; }
    public DateTime? Max { get; set; }
    public Func<DateOnly, bool>? Disable { get; set; }
    public int? MinuteStep { get; set; }

    private DateOnly _cursor;
    private bool _seeded;

    protected override Component? Render()
    {
        if (Native is true)
        {
            return NativeInput(InputType.DatetimeLocal);
        }

        var b = Resolve();
        var controlId = ControlId(b);
        var prefix = controlId ?? FallbackPrefix("bsdtp");
        var gridId = prefix + "-cal";
        var selected = ReadDateTime(b);
        var step = MinuteStep is { } s && s > 0 ? s : 5;
        SeedCursor(selected);

        var acc = b.Accessor;
        var ctx = b.Context;
        var fid = b.Field;
        var offset = CurrentOffset(b);

        var formatted = selected is { } dt ? dt.ToString("g", Culture) : string.Empty;

        var boxAria = new Dictionary<string, string?>
        {
            ["haspopup"] = "grid",
            ["expanded"] = Open ? "true" : "false",
            ["controls"] = gridId,
        };
        if (Open)
        {
            boxAria["activedescendant"] = PickerParts.CellId(prefix, _cursor);
        }

        var minDate = Min is { } mn ? DateOnly.FromDateTime(mn) : (DateOnly?)null;
        var maxDate = Max is { } mx ? DateOnly.FromDateTime(mx) : (DateOnly?)null;
        var selDate = selected is { } sv ? DateOnly.FromDateTime(sv) : (DateOnly?)null;
        var selTime = selected is { } st ? TimeOnly.FromDateTime(st) : (TimeOnly?)null;

        var popover = Div(Class: MenuClass())[
            PickerParts.MonthHeader(_cursor, Culture,
                () => _cursor = ClampCursor(_cursor.AddMonths(-1)),
                () => _cursor = ClampCursor(_cursor.AddMonths(1)),
                PrevMonthDisabled(_cursor), NextMonthDisabled(_cursor)),
            Div(Class: BsClass.Join("bs-datetime", Display.Flex(), Flex.Gap(2)))[
                PickerParts.CalendarGrid(_cursor, _cursor, selDate, minDate, maxDate, Disable, Culture,
                    prefix, gridId, day => PickDayAsync(acc, ctx, fid, selected, offset, day)),
                PickerParts.TimeColumns(selTime, step, Culture,
                    hour => PickTimeAsync(acc, ctx, fid, selected, offset, hour, null),
                    minute => PickTimeAsync(acc, ctx, fid, selected, offset, null, minute))
            ]
        ];

        return RenderShell(b, controlId, gridId, formatted, boxAria,
            raw => ParseAsync(acc, ctx, fid, offset, raw),
            OnKeyAsync,
            selected is not null, popover,
            () => WriteBoxedAsync(acc, ctx, fid, null));
    }

    private static DateTime? ReadDateTime(in Bound b) => (object?)b.Current switch
    {
        DateTime dt => dt,
        DateTimeOffset dto => dto.DateTime,
        _ => null,
    };

    // The offset to preserve when the bound type is DateTimeOffset: the current value's offset, else local.
    private static TimeSpan CurrentOffset(in Bound b) => (object?)b.Current switch
    {
        DateTimeOffset dto => dto.Offset,
        _ => TimeZoneInfo.Local.GetUtcOffset(DateTime.Now),
    };

    // Box the composed DateTime as the bound type (plain DateTime, or a DateTimeOffset preserving offset).
    // The target type comes from the accessor's real property type when bound (reflection — always the
    // model's actual type), so the boxed value can never mismatch the property under reflection SetValue.
    private static object BoxValue(DateTime value, TimeSpan offset, ExpressionAccessor.Accessor? acc) =>
        TargetUnderlying(acc) == typeof(DateTimeOffset)
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), offset)
            : value;

    private void SeedCursor(DateTime? selected, bool force = false)
    {
        if (_seeded && !force)
        {
            return;
        }

        _cursor = DateOnly.FromDateTime(selected ?? DateTime.Today);
        _seeded = true;
    }

    // The box is an editable input; typing commits live, so keyboard here is just Escape/Enter to close.
    private Task OnKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Escape" or "Enter")
        {
            Open = false;
            Text = null;
        }

        return Task.CompletedTask;
    }

    // Live per-keystroke parse of the typed text in the current culture: a valid date-time commits (boxed to
    // the bound type via the AOT-safe BoxValue) and moves the calendar cursor; empty clears a nullable picker.
    private Task ParseAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TimeSpan offset, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CanClear ? WriteBoxedAsync(acc, ctx, fid, null) : Task.CompletedTask;
        }

        if (DateTime.TryParse(raw, Culture, out var dt))
        {
            _cursor = ClampCursor(DateOnly.FromDateTime(dt));
            return WriteBoxedAsync(acc, ctx, fid, BoxValue(ClampValue(dt), offset, acc));
        }

        return Task.CompletedTask;
    }

    private async Task PickDayAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        DateTime? selected, TimeSpan offset, DateOnly day)
    {
        if (!Selectable(day))
        {
            return;
        }

        _cursor = day;
        var time = selected is { } s ? TimeOnly.FromDateTime(s) : new TimeOnly(0, 0);
        await WriteComposedAsync(acc, ctx, fid, day, time, offset).ConfigureAwait(false);
    }

    private Task PickTimeAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        DateTime? selected, TimeSpan offset, int? hour, int? minute)
    {
        // Keep the selected date, or the currently-viewed month cursor when there is no value yet (never
        // silently jump to today).
        var date = selected is { } s ? DateOnly.FromDateTime(s) : _cursor;
        var current = selected is { } t ? TimeOnly.FromDateTime(t) : new TimeOnly(0, 0);
        var time = new TimeOnly(hour ?? current.Hour, minute ?? current.Minute);
        return WriteComposedAsync(acc, ctx, fid, date, time, offset);
    }

    // Compose date + time into one value, clamped to [Min,Max] so the time half can't produce an
    // out-of-range DateTime on a boundary day, then write it back as the bound type.
    private Task WriteComposedAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        DateOnly date, TimeOnly time, TimeSpan offset)
    {
        Text = null;
        return WriteBoxedAsync(acc, ctx, fid, BoxValue(ClampValue(date.ToDateTime(time)), offset, acc));
    }

    private DateOnly ClampCursor(DateOnly d)
    {
        if (Min is { } mn && d < DateOnly.FromDateTime(mn))
        {
            d = DateOnly.FromDateTime(mn);
        }

        if (Max is { } mx && d > DateOnly.FromDateTime(mx))
        {
            d = DateOnly.FromDateTime(mx);
        }

        return d;
    }

    private DateTime ClampValue(DateTime v)
    {
        if (Min is { } mn && v < mn)
        {
            v = mn;
        }

        if (Max is { } mx && v > mx)
        {
            v = mx;
        }

        return v;
    }

    private bool Selectable(DateOnly d) =>
        !((Min is { } mn && d < DateOnly.FromDateTime(mn)) ||
          (Max is { } mx && d > DateOnly.FromDateTime(mx)) ||
          Disable?.Invoke(d) == true);

    private bool PrevMonthDisabled(DateOnly view) =>
        Min is { } mn && new DateOnly(view.Year, view.Month, 1).AddDays(-1) < DateOnly.FromDateTime(mn);

    private bool NextMonthDisabled(DateOnly view) =>
        Max is { } mx && new DateOnly(view.Year, view.Month, 1).AddMonths(1) > DateOnly.FromDateTime(mx);
}
