using System.Globalization;

namespace Rask;

// The typed trigger: hour, minute and AM/PM fields in the input's box, and the list beside them.
public sealed partial class UiTimePicker<T>
{
    // What has been typed so far, kept until hour and minute make a time.
    private int? _hour;
    private int? _minute;
    private bool? _afternoon;

    private Component TypedTrigger(View view)
    {
        var twelve = UiTimePickerLook.TwelveHour(Format, Culture);
        var parts = Parts(view, twelve);
        var opens = Dropdown != false && Disabled != true;

        // The room after the fields opens the list, as a press on Flux's trigger does. C# cannot call
        // showPopover(): the runtime shows the list when `data-rask-popover-open` changes. It only opens — a
        // press outside the list is the browser's own dismissal, and this press is one.
        var box = Div
            .Class(UiClass.Compose(UiTimePickerLook.InputBox, UiTimePickerLook.BoxSize(Size ?? Ui.TimePickerSize.Base)))
            .Data(view.Field.Invalid ? UiTimePickerLook.InvalidBoxMarks : UiTimePickerLook.BoxMarks)
            .Attributes(("style", "anchor-name:--" + ListId));

        return box[
            Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiTimePickerLook.InputIcon),
            Div.Class(UiTimePickerLook.Segments).Attributes(("dir", "ltr"))[
                Segment(view, Part.Hour, parts.Hour, "Hour", "hh", "ui-hour-input").Id(view.Field.ControlId),
                ":",
                Segment(view, Part.Minute, parts.Minute, "Minute", "mm", "ui-minute-input"),
                twelve ? "\u00A0" : null,
                twelve ? Segment(view, Part.Period, parts.Period, "AM/PM", UiTimePickerLook.Period(false, Culture), "ui-meridiem-input") : null
            ],
            opens ? Span.Class("grow").OnClick(() => _open = true) : Span.Class("grow"),
            Dropdown != false ? Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiTimePickerLook.InputChevron) : null
        ];
    }

    private HTMLInputElement<string> Segment(View view, Part part, string text, string label, string placeholder, string mark)
    {
        var input = Input
            .Value(text)
            .OnChange(typed => TypedAsync(view, part, typed))
            .Type(InputType.Text)
            .Placeholder(placeholder)
            .Disabled(Disabled == true)
            .Class(UiTimePickerLook.Segment)
            .Data(mark, null)
            .Aria(Named(view, part, label))
            .OnKeyDown(e => e.Key switch
            {
                Keys.ArrowUp => NudgeAsync(view, part, 1),
                Keys.ArrowDown => NudgeAsync(view, part, -1),
                _ => Task.CompletedTask,
            });

        return part == Part.Period ? input : input.Attributes(("inputmode", "numeric"));
    }

    // Every field is named; the hour, which the label points at, also says what the field says about the control.
    private static Dictionary<string, string?> Named(View view, Part part, string label) =>
        new(part == Part.Hour ? view.Field.Aria : [], StringComparer.Ordinal) { ["label"] = label };

    // The three fields as text: the chosen time's, or what has been typed towards one.
    private (string Hour, string Minute, string Period) Parts(View view, bool twelve)
    {
        var (hour, minute, afternoon) = Typing(view);
        var untouched = hour is null && minute is null && afternoon is null;
        return (
            hour is { } h ? Clock(h, twelve).ToString("00", CultureInfo.InvariantCulture) : string.Empty,
            minute?.ToString("00", CultureInfo.InvariantCulture) ?? string.Empty,
            untouched ? string.Empty : UiTimePickerLook.Period(afternoon == true, Culture));
    }

    // Hour (0–23), minute and half of the day: from the chosen time, else from what was typed.
    private (int? Hour, int? Minute, bool? Afternoon) Typing(View view) =>
        view.Chosen.Count > 0
            ? (view.Chosen[0].Hour, view.Chosen[0].Minute, view.Chosen[0].Hour >= 12)
            : (_hour, _minute, _afternoon);

    private Task TypedAsync(View view, Part part, string typed)
    {
        var twelve = UiTimePickerLook.TwelveHour(Format, Culture);
        var (hour, minute, afternoon) = Typing(view);
        var digits = int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : (int?)null;

        switch (part)
        {
            case Part.Hour:
                hour = Hour(digits, afternoon == true, twelve);
                break;
            case Part.Minute:
                minute = digits is >= 0 and <= 59 ? digits : null;
                break;
            default:
                afternoon = typed.StartsWith(UiTimePickerLook.Period(true, Culture)[..1], StringComparison.CurrentCultureIgnoreCase);
                hour = Half(hour, afternoon == true);
                break;
        }

        return SettleAsync(view, hour, minute, afternoon);
    }

    // ArrowUp and ArrowDown step the field they are pressed in, round the clock.
    private Task NudgeAsync(View view, Part part, int by)
    {
        var (hour, minute, afternoon) = Typing(view);
        switch (part)
        {
            case Part.Hour:
                hour = ((hour ?? 0) + by + 24) % 24;
                afternoon = hour >= 12;
                break;
            case Part.Minute:
                minute = ((minute ?? 0) + by + 60) % 60;
                break;
            default:
                afternoon = afternoon != true;
                hour = Half(hour, afternoon == true);
                break;
        }

        return SettleAsync(view, hour, minute, afternoon);
    }

    // Hour and minute together are a time and are committed; either alone waits for the other.
    private Task SettleAsync(View view, int? hour, int? minute, bool? afternoon)
    {
        (_hour, _minute, _afternoon) = (hour, minute, afternoon);
        if (hour is not { } h || minute is not { } m)
        {
            return view.Chosen.Count > 0 && UiTimePickerValue.Clears<T>() ? CommitAsync(view, []) : Task.CompletedTask;
        }

        var time = new TimeOnly(h, m);
        return Off(time) ? Task.CompletedTask : CommitAsync(view, [time]);
    }

    private static int? Hour(int? typed, bool afternoon, bool twelve)
    {
        if (!twelve)
        {
            return typed is >= 0 and <= 23 ? typed : null;
        }

        return typed is >= 1 and <= 12 ? Half(typed, afternoon) : null;
    }

    // The hour as a 12-hour clock writes it.
    private static int Clock(int hour, bool twelve) => twelve ? ((hour + 11) % 12) + 1 : hour;

    private static int Afternoon(bool afternoon) => afternoon ? 12 : 0;

    // An hour moved into the morning or the afternoon.
    private static int? Half(int? hour, bool afternoon) => hour is { } h ? (h % 12) + Afternoon(afternoon) : null;

    private enum Part
    {
        Hour,
        Minute,
        Period,
    }
}
