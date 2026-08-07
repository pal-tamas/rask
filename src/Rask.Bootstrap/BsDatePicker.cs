using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap date picker: a .form-control trigger that opens a custom month-grid calendar popover,
// bound to a DateOnly (or DateOnly?). Open/close, month navigation and day selection are pure live-diff
// view state — no bootstrap.js. Full keyboard support (arrows move a virtual cursor via
// aria-activedescendant, PageUp/Down change month — Shift for a year, Home/End the week edge, Enter
// selects, Escape closes) and ARIA grid roles; typing into the box also commits live. Min/Max/Disable grey
// out unavailable days. Labels localizes the nav/clear aria-labels. Native:true falls back to <input type=date>.
//   Bound:      BsDatePicker(() => model.StartDate, Label: "Start", Min: today)
//   Controlled: BsDatePicker(Value: d, OnChange: v => …)
public sealed partial class BsDatePicker<T> : BsPickerBase<T>
{
    public DateOnly? Min { get; set; }
    public DateOnly? Max { get; set; }
    public Func<DateOnly, bool>? Disable { get; set; }

    private DateOnly _cursor;
    private bool _seeded;

    // True once the user has arrow-navigated the open grid, so Enter commits the highlighted day. Reset by
    // any typing (ParseAsync) and on close, so Enter after clearing a nullable field never re-writes a value.
    private bool _navigated;

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

        var formatted = selected is { } s ? s.ToString("d", Culture) : string.Empty;

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
                PrevMonthDisabled(_cursor), NextMonthDisabled(_cursor), PickerLabels),
            PickerParts.CalendarGrid(_cursor, _cursor, selected, Min, Max, Disable, Culture, prefix, gridId,
                day => PickAsync(acc, ctx, fid, day))
        ];

        return RenderShell(b, controlId, gridId, formatted, boxAria,
            raw => ParseAsync(acc, ctx, fid, raw),
            e => OnKeyAsync(acc, ctx, fid, e),
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

    // Grid keyboard navigation over a virtual cursor (the box keeps DOM focus; aria-activedescendant points
    // at the cursor cell — the WAI-ARIA combobox+grid pattern, same as BsSelect). A first navigation key opens
    // the popover; thereafter arrows move a day/week, PageUp/Down a month (Shift a year), Home/End the week
    // edge (shared GridMove), Enter selects the navigated cursor day, Escape closes. Moves clamp to [Min,Max];
    // the cursor may rest on a Disable-predicate day but Enter won't commit it (guarded by Selectable).
    private async Task OnKeyAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, KeyboardEventArgs e)
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
            _cursor = Clamp(moved);
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
                    await PickAsync(acc, ctx, fid, _cursor).ConfigureAwait(false);
                }
                else
                {
                    Open = false;
                    Text = null;
                }

                break;
        }
    }

    // Live per-keystroke parse of the typed text in the current culture: a valid, in-range date commits and
    // moves the calendar cursor; empty clears a nullable picker; anything else is left as-is (keep typing).
    private Task ParseAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, string raw)
    {
        // Typing is text entry, not grid navigation: drop the nav flag so a later Enter closes rather than
        // re-committing a stale cursor day (e.g. after the user clears a nullable field to leave it blank).
        _navigated = false;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return CanClear ? WriteBoxedAsync(acc, ctx, fid, null) : Task.CompletedTask;
        }

        if (DateOnly.TryParse(raw, Culture, out var d) && Selectable(d))
        {
            _cursor = Clamp(d);
            return WriteBoxedAsync(acc, ctx, fid, d);
        }

        return Task.CompletedTask;
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
        Text = null;
        _navigated = false;
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
