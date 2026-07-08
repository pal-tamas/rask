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

        var content = Span(Class: selected is null ? Txt.Muted : null)[
            selected is { } t ? t.ToString("t", Culture) : Placeholder ?? Culture.DateTimeFormat.ShortTimePattern];

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

        return RenderShell(b, controlId, gridId, content, boxAria,
            () => Open = !Open,
            e => OnKeyAsync(acc, ctx, fid, selected, step, e),
            selected is not null, popover,
            () => WriteBoxedAsync(acc, ctx, fid, null));
    }

    private static TimeOnly? ReadTime(in Bound b) =>
        (object?)b.Current is TimeOnly t ? t : null;

    private async Task OnKeyAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        TimeOnly? selected, int step, KeyboardEventArgs e)
    {
        if (!Open)
        {
            if (e.Key is "Enter" or " " or "ArrowDown")
            {
                Open = true;
            }

            return;
        }

        var current = selected ?? new TimeOnly(0, 0);
        switch (e.Key)
        {
            case "Escape":
            case "Enter":
                Open = false;
                break;
            case "ArrowUp":
                await WriteAsync(acc, ctx, fid, current.AddMinutes(step)).ConfigureAwait(false);
                break;
            case "ArrowDown":
                await WriteAsync(acc, ctx, fid, current.AddMinutes(-step)).ConfigureAwait(false);
                break;
            case "PageUp":
                await WriteAsync(acc, ctx, fid, current.AddHours(1)).ConfigureAwait(false);
                break;
            case "PageDown":
                await WriteAsync(acc, ctx, fid, current.AddHours(-1)).ConfigureAwait(false);
                break;
        }
    }

    private Task WriteTimeAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid,
        TimeOnly? selected, int? hour, int? minute)
    {
        var current = selected ?? new TimeOnly(0, 0);
        var next = new TimeOnly(hour ?? current.Hour, minute ?? current.Minute);
        return WriteAsync(acc, ctx, fid, next);
    }

    private Task WriteAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TimeOnly value) =>
        WriteBoxedAsync(acc, ctx, fid, value);
}
