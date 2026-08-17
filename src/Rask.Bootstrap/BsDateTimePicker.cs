using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap date+time picker: a .form-control trigger that opens a popover pairing the month-grid
// calendar with the hour/minute lists, bound to a DateTime (or DateTime?/DateTimeOffset/DateTimeOffset?).
// Pure live-diff view state — no bootstrap.js. Picking a day preserves the time-of-day; picking an
// hour/minute/second preserves the date, so the halves compose one value. Min/Max/Disable constrain the
// calendar, and the time columns grey out-of-range items on the boundary day. Seconds:true adds a seconds
// column. Keyboard mirrors BsDatePicker for the grid; Labels localizes the nav/column/clear aria-labels.
// Native:true falls back to <input type=datetime-local>. Reuses PickerParts.CalendarGrid + TimeColumns.
//   Bound:      BsDateTimePicker.Bind(() => model.When).Label("When")
//   Controlled: BsDateTimePicker<DateTime>().Value(dt).OnChange(v => …)

/// <summary>
///     A combined date and time picker, bound to a model field.
/// </summary>
public sealed partial class BsDateTimePicker<T> : BsPickerBase<T>
{
    /// <summary>The earliest selectable instant.</summary>
    public DateTime? Min { get; set; }

    /// <summary>The latest selectable instant.</summary>
    public DateTime? Max { get; set; }

    /// <summary>Decides, per date, whether it can be chosen.</summary>
    public Func<DateOnly, bool>? Disable { get; set; }

    /// <summary>The granularity of the minute list.</summary>
    public int? MinuteStep { get; set; }

    /// <summary>Includes a seconds column.</summary>
    public bool? Seconds { get; set; }

    /// <summary>The granularity of the seconds list.</summary>
    public int? SecondStep { get; set; }

    private DateOnly _cursor;
    private bool _seeded;

    // True once the user has arrow-navigated the open grid, so Enter commits the highlighted day. Reset by
    // any typing (ParseAsync) and on close, so Enter after clearing a nullable field never re-writes a value.
    private bool _navigated;

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
        var secStep = SecondStep is { } ss && ss > 0 ? ss : 5;
        var showSeconds = Seconds is true;
        SeedCursor(selected);

        var acc = b.Accessor;
        var ctx = b.Context;
        var fid = b.Field;
        var offset = CurrentOffset(b);

        var formatted = selected is { } dt ? dt.ToString(showSeconds ? "G" : "g", Culture) : string.Empty;

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

        // The time columns constrain only on the boundary day: on Min's date the earliest time is Min's
        // time-of-day (symmetric at Max), otherwise the whole day is available. The date the time applies
        // to is the selected date, or the viewed cursor when there is no value yet.
        var timeDate = selDate ?? _cursor;
        var minTime = Min is { } mnv && timeDate == DateOnly.FromDateTime(mnv)
            ? TimeOnly.FromDateTime(mnv) : (TimeOnly?)null;
        var maxTime = Max is { } mxv && timeDate == DateOnly.FromDateTime(mxv)
            ? TimeOnly.FromDateTime(mxv) : (TimeOnly?)null;

        var popover = Div.Class(MenuClass())[
            PickerParts.MonthHeader(_cursor, Culture,
                () => _cursor = ClampCursor(_cursor.AddMonths(-1)),
                () => _cursor = ClampCursor(_cursor.AddMonths(1)),
                PrevMonthDisabled(_cursor), NextMonthDisabled(_cursor), PickerLabels),
            Div.Class(BsClass.Join("bs-datetime", Display.Flex(), Flex.Gap(2)))[
                PickerParts.CalendarGrid(_cursor, _cursor, selDate, minDate, maxDate, Disable, Culture,
                    prefix, gridId, day => PickDayAsync(acc, ctx, fid, selected, offset, day)),
                PickerParts.TimeColumns(selTime, step, showSeconds, secStep, minTime, maxTime, Culture,
                    PickerLabels,
                    hour => PickTimeAsync(acc, ctx, fid, selected, offset, hour, null, null),
                    minute => PickTimeAsync(acc, ctx, fid, selected, offset, null, minute, null),
                    showSeconds ? second => PickTimeAsync(acc, ctx, fid, selected, offset, null, null, second) : null)
            ]
        ];

        return RenderShell(b, controlId, gridId, formatted, boxAria,
            raw => ParseAsync(acc, ctx, fid, offset, raw),
            e => OnKeyAsync(acc, ctx, fid, selected, offset, e),
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

    // Grid keyboard navigation, mirroring BsDatePicker via the shared GridMove/IsGridOpenKey (the box keeps
    // focus; aria-activedescendant points at the cursor cell). A first navigation key opens; arrows move a
    // day/week, PageUp/Down a month (Shift a year), Home/End the week edge, Enter commits the navigated cursor
    // day preserving the time-of-day (the popover stays open so the time half can still be chosen), Escape
    // closes. Moves clamp to the calendar range.
    private async Task OnKeyAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        DateTime? selected, TimeSpan offset, KeyboardEventArgs e)
    {
        if (!Open)
        {
            if (IsGridOpenKey(e))
            {
                Open = true;
            }

            return;
        }

        if (GridMove(_cursor, e) is { } moved)
        {
            _cursor = ClampCursor(moved);
            _navigated = true;
            return;
        }

        switch (e.Key)
        {
            case "Escape":
                Open = false;
                Text = null;
                _navigated = false;
                break;
            case "Enter":
                // Commit the highlighted day only when the user actually arrow-navigated to it; otherwise just
                // close, so Enter after clearing a nullable field doesn't re-write the stale cursor day.
                if (_navigated && Selectable(_cursor))
                {
                    await PickDayAsync(acc, ctx, fid, selected, offset, _cursor).ConfigureAwait(false);
                }
                else
                {
                    Open = false;
                    Text = null;
                }

                break;
        }
    }

    // Live per-keystroke parse of the typed text in the current culture: a valid date-time commits (boxed to
    // the bound type via the AOT-safe BoxValue) and moves the calendar cursor; empty clears a nullable picker.
    private Task ParseAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TimeSpan offset, string raw)
    {
        // Typing is text entry, not grid navigation: drop the nav flag so a later Enter closes rather than
        // re-committing a stale cursor day.
        _navigated = false;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return CanClear ? WriteBoxedAsync(acc, ctx, fid, null) : Task.CompletedTask;
        }

        if (DateTime.TryParse(raw, Culture, out var dt))
        {
            // Drop sub-minute precision unless the picker shows seconds, matching the click path — otherwise a
            // typed "…:30:45" would store 45s that the "g" display hides and no click could ever produce.
            dt = NormalizeSeconds(dt);
            _cursor = ClampCursor(DateOnly.FromDateTime(dt));
            return WriteBoxedAsync(acc, ctx, fid, BoxValue(ClampValue(dt), offset, acc));
        }

        return Task.CompletedTask;
    }

    // Truncate to whole-minute precision unless the picker shows seconds (preserves DateTimeKind).
    private DateTime NormalizeSeconds(DateTime dt) =>
        Seconds is true ? dt : dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerMinute));

    private async Task PickDayAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        DateTime? selected, TimeSpan offset, DateOnly day)
    {
        if (!Selectable(day))
        {
            return;
        }

        _cursor = day;
        _navigated = false;
        var time = selected is { } s ? TimeOnly.FromDateTime(s) : new TimeOnly(0, 0);
        await WriteComposedAsync(acc, ctx, fid, day, time, offset).ConfigureAwait(false);
    }

    private Task PickTimeAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        DateTime? selected, TimeSpan offset, int? hour, int? minute, int? second)
    {
        // Keep the selected date, or the currently-viewed month cursor when there is no value yet (never
        // silently jump to today).
        var date = selected is { } s ? DateOnly.FromDateTime(s) : _cursor;
        var current = selected is { } t ? TimeOnly.FromDateTime(t) : new TimeOnly(0, 0);
        var time = new TimeOnly(hour ?? current.Hour, minute ?? current.Minute, second ?? current.Second);

        // Drop sub-minute precision unless the picker shows seconds, so a minute-precision picker never
        // composes a stray seconds component.
        if (Seconds is not true)
        {
            time = new TimeOnly(time.Hour, time.Minute);
        }

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
