using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Bootstrap;

// A Bootstrap time picker: a .form-control trigger that opens a custom popover with scrollable hour and
// minute lists, bound to a TimeOnly (or TimeOnly?). Open/close and selection are pure live-diff view
// state — no bootstrap.js. Minutes step by MinuteStep (default 5). Keyboard: Enter/ArrowDown open,
// ArrowUp/Down nudge the minute by a step, PageUp/Down nudge the hour, Escape closes. Native:true falls
// back to <input type=time>.
//   Bound:      BsTimePicker(() => model.Alarm, Label: "Alarm", MinuteStep: 15)
//   Controlled: BsTimePicker(Value: t, OnChange: v => …)
public sealed class BsTimePicker<T> : BsPickerBase<T>
{
    public int? MinuteStep { get; set; }

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
        var step = MinuteStep is { } s && s > 0 ? s : 5;

        var acc = b.Accessor;
        var ctx = b.Context;
        var fid = b.Field;

        var formatted = selected is { } t ? t.ToString("t", Culture) : string.Empty;

        var boxAria = new Dictionary<string, string?>
        {
            ["haspopup"] = "listbox",
            ["expanded"] = Open ? "true" : "false",
            ["controls"] = gridId,
        };

        var popover = Div(Id: gridId, Class: MenuClass())[
            PickerParts.TimeColumns(selected, step, Culture,
                hour => WriteTimeAsync(acc, ctx, fid, selected, hour, null),
                minute => WriteTimeAsync(acc, ctx, fid, selected, null, minute))
        ];

        return RenderShell(b, controlId, gridId, formatted, boxAria,
            raw => ParseAsync(acc, ctx, fid, raw),
            OnKeyAsync,
            selected is not null, popover,
            () => WriteBoxedAsync(acc, ctx, fid, null));
    }

    private static TimeOnly? ReadTime(in Bound b) =>
        (object?)b.Current is TimeOnly t ? t : null;

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

    // Live per-keystroke parse of the typed text in the current culture; empty clears a nullable picker.
    private Task ParseAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CanClear ? WriteBoxedAsync(acc, ctx, fid, null) : Task.CompletedTask;
        }

        return TimeOnly.TryParse(raw, Culture, out var t)
            ? WriteBoxedAsync(acc, ctx, fid, t)
            : Task.CompletedTask;
    }

    private Task WriteTimeAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        TimeOnly? selected, int? hour, int? minute)
    {
        var current = selected ?? new TimeOnly(0, 0);
        var next = new TimeOnly(hour ?? current.Hour, minute ?? current.Minute);
        Text = null;
        return WriteBoxedAsync(acc, ctx, fid, next);
    }
}
