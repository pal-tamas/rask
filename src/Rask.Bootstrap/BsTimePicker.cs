using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap time picker: a .form-control trigger that opens a custom popover with scrollable hour and
// minute lists (plus seconds when Seconds:true), bound to a TimeOnly (or TimeOnly?). Open/close and
// selection are pure live-diff view state — no bootstrap.js. Minutes step by MinuteStep (default 5),
// seconds by SecondStep (default 5). Min/Max grey out-of-range items and clamp every write. Keyboard: a
// first nav key opens; ArrowUp/Down nudge the minute by a step (Shift+ArrowUp/Down the second when Seconds
// is on), PageUp/Down nudge the hour, Home/End jump to the earliest/latest selectable time, Enter/Escape
// close; typing into the box also commits live. Labels localizes the column/clear aria-labels. Native:true
// falls back to <input type=time>.
//   Bound:      BsTimePicker(() => model.Alarm, Label: "Alarm", MinuteStep: 15)
//   Controlled: BsTimePicker(Value: t, OnChange: v => …)
public sealed class BsTimePicker<T> : BsPickerBase<T>
{
    public int? MinuteStep { get; set; }
    public TimeOnly? Min { get; set; }
    public TimeOnly? Max { get; set; }
    public bool? Seconds { get; set; }
    public int? SecondStep { get; set; }

    // The effective minute/second steps (default 5), shared by the render and the keyboard nudge.
    private int Step => MinuteStep is { } s && s > 0 ? s : 5;
    private int SecStep => SecondStep is { } ss && ss > 0 ? ss : 5;

    protected override Component? Render()
    {
        if (Native is true)
        {
            return NativeInput(InputType.Time);
        }

        var b = Resolve();
        var controlId = ControlId(b);
        var gridId = (controlId ?? FallbackPrefix("bstp")) + "-time";
        var selected = ReadTime(b);
        var step = Step;
        var secStep = SecStep;
        var showSeconds = Seconds is true;

        var acc = b.Accessor;
        var ctx = b.Context;
        var fid = b.Field;

        var formatted = selected is { } t ? t.ToString(showSeconds ? "T" : "t", Culture) : string.Empty;

        var boxAria = new Dictionary<string, string?>
        {
            ["haspopup"] = "listbox",
            ["expanded"] = Open ? "true" : "false",
            ["controls"] = gridId,
        };

        var popover = Div(Id: gridId, Class: MenuClass())[
            PickerParts.TimeColumns(selected, step, showSeconds, secStep, Min, Max, Culture, PickerLabels,
                hour => WriteTimeAsync(acc, ctx, fid, selected, hour, null, null),
                minute => WriteTimeAsync(acc, ctx, fid, selected, null, minute, null),
                showSeconds ? second => WriteTimeAsync(acc, ctx, fid, selected, null, null, second) : null)
        ];

        return RenderShell(b, controlId, gridId, formatted, boxAria,
            raw => ParseAsync(acc, ctx, fid, raw),
            e => OnKeyAsync(acc, ctx, fid, selected, e),
            selected is not null, popover,
            () => WriteBoxedAsync(acc, ctx, fid, null));
    }

    private static TimeOnly? ReadTime(in Bound b) =>
        (object?)b.Current is TimeOnly t ? t : null;

    // A first nav key opens; then ArrowUp/Down nudge the minute by a step (Shift+ArrowUp/Down the second when
    // Seconds is on), PageUp/Down nudge the hour, Home/End jump to the earliest/latest selectable time, and
    // Enter/Escape close. Nudges wrap within the day and clamp to [Min,Max]; typing commits live separately.
    private async Task OnKeyAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        TimeOnly? selected, KeyboardEventArgs e)
    {
        if (!Open)
        {
            // Enter and Space are excluded: Enter is the form's submit key (and the client only contains it
            // while the popover is already open), and Space is a literal text character in the editable box.
            if (e.Key is "ArrowDown" or "ArrowUp" or "PageDown" or "PageUp" or "Home" or "End")
            {
                Open = true;
            }

            return;
        }

        var step = Step;
        var seconds = Seconds is true;
        var cur = selected ?? new TimeOnly(0, 0);
        switch (e.Key)
        {
            case "Escape":
            case "Enter":
                Open = false;
                Text = null;
                break;
            case "ArrowDown":
                await NudgeAsync(acc, ctx, fid,
                    seconds && e.Shift ? cur.Add(TimeSpan.FromSeconds(SecStep)) : cur.AddMinutes(step))
                    .ConfigureAwait(false);
                break;
            case "ArrowUp":
                await NudgeAsync(acc, ctx, fid,
                    seconds && e.Shift ? cur.Add(TimeSpan.FromSeconds(-SecStep)) : cur.AddMinutes(-step))
                    .ConfigureAwait(false);
                break;
            case "PageDown":
                await NudgeAsync(acc, ctx, fid, cur.AddHours(1)).ConfigureAwait(false);
                break;
            case "PageUp":
                await NudgeAsync(acc, ctx, fid, cur.AddHours(-1)).ConfigureAwait(false);
                break;
            case "Home":
                await NudgeAsync(acc, ctx, fid, Earliest()).ConfigureAwait(false);
                break;
            case "End":
                await NudgeAsync(acc, ctx, fid, Latest()).ConfigureAwait(false);
                break;
        }
    }

    // The earliest/latest selectable time for Home/End: the Min/Max bound, or the day edge (00:00, and 23:59
    // or 23:59:59 with seconds). NudgeAsync clamps + normalizes, so these are exact whatever the precision.
    private TimeOnly Earliest() => Min ?? new TimeOnly(0, 0, 0);

    private TimeOnly Latest() => Max ?? (Seconds is true ? new TimeOnly(23, 59, 59) : new TimeOnly(23, 59));

    // Live per-keystroke parse of the typed text in the current culture; empty clears a nullable picker.
    // An out-of-[Min,Max] value is ignored (keep typing) rather than committed.
    private Task ParseAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CanClear ? WriteBoxedAsync(acc, ctx, fid, null) : Task.CompletedTask;
        }

        // Clamp after Normalize: dropping seconds off an in-range value can push it below Min (e.g. Min
        // 09:30:30, typed 09:30:45 → 09:30:00), so re-bound it exactly as the click/nudge paths do.
        return TimeOnly.TryParse(raw, Culture, out var t) && Selectable(t)
            ? WriteBoxedAsync(acc, ctx, fid, Clamp(Normalize(t)))
            : Task.CompletedTask;
    }

    private Task WriteTimeAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        TimeOnly? selected, int? hour, int? minute, int? second)
    {
        var current = selected ?? new TimeOnly(0, 0);
        var next = new TimeOnly(hour ?? current.Hour, minute ?? current.Minute, second ?? current.Second);
        Text = null;
        return WriteBoxedAsync(acc, ctx, fid, Clamp(Normalize(next)));
    }

    private Task NudgeAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TimeOnly next)
    {
        Text = null;
        return WriteBoxedAsync(acc, ctx, fid, Clamp(Normalize(next)));
    }

    // Drop sub-minute precision unless the picker shows seconds, so a minute-precision picker never writes
    // a stray seconds component.
    private TimeOnly Normalize(TimeOnly t) =>
        Seconds is true ? t : new TimeOnly(t.Hour, t.Minute);

    private bool Selectable(TimeOnly t) =>
        !((Min is { } mn && t < mn) || (Max is { } mx && t > mx));

    private TimeOnly Clamp(TimeOnly t)
    {
        if (Min is { } mn && t < mn)
        {
            t = mn;
        }

        if (Max is { } mx && t > mx)
        {
            t = mx;
        }

        return t;
    }
}
