using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap date picker: a .form-control trigger that opens a custom month-grid calendar popover,
// bound to a DateOnly (or DateOnly?). Open/close, month navigation and day selection are pure live-diff
// view state — no bootstrap.js. Full keyboard support (arrows move a virtual cursor via
// aria-activedescendant, PageUp/Down change month, Home/End the week, Enter selects, Escape closes) and
// ARIA grid roles. Min/Max/Disable grey out unavailable days. Native:true falls back to <input type=date>.
//   Bound:      BsDatePicker(() => model.StartDate, Label: "Start", Min: today)
//   Controlled: BsDatePicker(Value: d, OnChange: v => …)
public sealed class BsDatePicker<T> : BsPickerBase<T>
{
    public DateOnly? Min { get; set; }
    public DateOnly? Max { get; set; }
    public Func<DateOnly, bool>? Disable { get; set; }

    private DateOnly _cursor;
    private bool _seeded;

    protected override Component? Render()
    {
        if (Native is true)
        {
            return NativeInput(InputType.Date);
        }

        var b = Resolve();
        var controlId = ControlId(b);
        var prefix = controlId ?? FallbackPrefix("bsdp");
        var gridId = prefix + "-cal";
        var selected = ReadDate(b);
        SeedCursor(selected);

        var acc = b.Accessor;
        var ctx = b.Context;
        var fid = b.Field;

        var content = Span(Class: selected is null ? Txt.Muted : null)[
            selected is { } s ? s.ToString("d", Culture) : Placeholder ?? Culture.DateTimeFormat.ShortDatePattern];

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

        var popover = Div(Class: MenuClass())[
            PickerParts.MonthHeader(_cursor, Culture,
                () => _cursor = Clamp(_cursor.AddMonths(-1)),
                () => _cursor = Clamp(_cursor.AddMonths(1)),
                PrevMonthDisabled(_cursor), NextMonthDisabled(_cursor)),
            PickerParts.CalendarGrid(_cursor, _cursor, selected, Min, Max, Disable, Culture, prefix, gridId,
                day => PickAsync(acc, ctx, fid, day))
        ];

        return RenderShell(b, controlId, gridId, content, boxAria,
            () => Toggle(selected),
            e => OnKeyAsync(acc, ctx, fid, selected, e),
            selected is not null, popover,
            () => WriteBoxedAsync(acc, ctx, fid, null));
    }

    private static DateOnly? ReadDate(in Bound b) =>
        (object?)b.Current is DateOnly d ? d : null;

    private void SeedCursor(DateOnly? selected, bool force = false)
    {
        if (_seeded && !force)
        {
            return;
        }

        _cursor = Clamp(selected ?? DateOnly.FromDateTime(DateTime.Today));
        _seeded = true;
    }

    private void Toggle(DateOnly? selected)
    {
        if (!Open)
        {
            SeedCursor(selected, force: true);
        }

        Open = !Open;
    }

    private async Task OnKeyAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        DateOnly? selected, KeyboardEventArgs e)
    {
        if (!Open)
        {
            if (e.Key is "Enter" or " " or "ArrowDown")
            {
                // Re-seed the cursor to the current value/today on open (mouse Toggle does the same), so a
                // value changed elsewhere while closed isn't overwritten by a stale cursor on Enter.
                SeedCursor(selected, force: true);
                Open = true;
            }

            return;
        }

        switch (e.Key)
        {
            case "Escape":
                Open = false;
                break;
            case "ArrowLeft":
                _cursor = Clamp(_cursor.AddDays(-1));
                break;
            case "ArrowRight":
                _cursor = Clamp(_cursor.AddDays(1));
                break;
            case "ArrowUp":
                _cursor = Clamp(_cursor.AddDays(-7));
                break;
            case "ArrowDown":
                _cursor = Clamp(_cursor.AddDays(7));
                break;
            case "PageUp":
                _cursor = Clamp(_cursor.AddMonths(e.Shift ? -12 : -1));
                break;
            case "PageDown":
                _cursor = Clamp(_cursor.AddMonths(e.Shift ? 12 : 1));
                break;
            case "Home":
                _cursor = Clamp(PickerParts.WeekStart(_cursor, Culture));
                break;
            case "End":
                _cursor = Clamp(PickerParts.WeekEnd(_cursor, Culture));
                break;
            case "Enter":
            case " ":
                if (Selectable(_cursor))
                {
                    await PickAsync(acc, ctx, fid, _cursor).ConfigureAwait(false);
                }

                break;
        }
    }

    private async Task PickAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, DateOnly day)
    {
        if (!Selectable(day))
        {
            return;
        }

        _cursor = day;
        Open = false;
        await WriteBoxedAsync(acc, ctx, fid, day).ConfigureAwait(false);
    }

    private DateOnly Clamp(DateOnly d)
    {
        if (Min is { } mn && d < mn)
        {
            d = mn;
        }

        if (Max is { } mx && d > mx)
        {
            d = mx;
        }

        return d;
    }

    private bool Selectable(DateOnly d) =>
        !((Min is { } mn && d < mn) || (Max is { } mx && d > mx) || Disable?.Invoke(d) == true);

    private bool PrevMonthDisabled(DateOnly view) =>
        Min is { } mn && new DateOnly(view.Year, view.Month, 1).AddDays(-1) < mn;

    private bool NextMonthDisabled(DateOnly view) =>
        Max is { } mx && new DateOnly(view.Year, view.Month, 1).AddMonths(1) > mx;
}
