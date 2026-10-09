using System.Globalization;

namespace Rask;

// The typed trigger: hour, minute and AM/PM fields in the input's box, and the list beside them.
public sealed partial class UiTimePicker<T>
{
    private const string Clock = "HH:mm";

    // The three fields are typed as ONE: the runtime (`data-rask-segments`) takes the digits, moves on from a
    // part that can take no more, steps a part on the vertical arrows and sets AM or PM on `a` and `p`, as
    // Flux's fields do. A whole time arrives through the one hidden field, as 24-hour `HH:mm`.
    private Component TypedTrigger(View view)
    {
        var twelve = UiTimePickerLook.TwelveHour(Format, Culture);
        var opens = Dropdown != false && Disabled != true;
        var hour = RaskStrings.Get(RaskString.PickerHour, "Hour");

        var box = Div
            .Class(UiClass.Compose(UiTimePickerLook.InputBox, UiTimePickerLook.BoxSize(Size ?? Ui.TimePickerSize.Base)))
            .Data(view.Field.Invalid ? UiTimePickerLook.InvalidBoxMarks : UiTimePickerLook.BoxMarks);

        // Everything in the box but a field opens the list, and a second press closes it, as on Flux: the
        // runtime's toggle, which leaves a press in a field to the field.
        box = opens
            ? box.Attributes(("data-rask-toggle", ListId), ("style", "anchor-name:--" + ListId))
            : box.Attributes(("style", "anchor-name:--" + ListId));

        return box[
            Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiTimePickerLook.InputIcon),
            Div.Class(UiTimePickerLook.Segments).Data(UiTimePickerLook.SegmentsMark).Attributes(("dir", "ltr"))[
                Segment("hour", hour, RaskStrings.Get(RaskString.TimePickerHourPlaceholder, "hh")).Id(view.Field.ControlId).Aria(Named(view, hour)).Attributes(UiTimePickerLook.Numeric),
                ":",
                Segment("minute", RaskStrings.Get(RaskString.PickerMinute, "Minute"), RaskStrings.Get(RaskString.TimePickerMinutePlaceholder, "mm")).Attributes(UiTimePickerLook.Numeric),
                twelve ? "\u00A0" : null,
                twelve ? Segment("meridiem", RaskStrings.Get(RaskString.TimePickerMeridiem, "AM/PM"), UiTimePickerLook.Period(false, Culture)) : null,
                Whole(view)
            ],
            Span.Class("grow"),
            Dropdown != false ? Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiTimePickerLook.InputChevron) : null
        ];
    }

    // A part is the browser's own: no value and no handler, so a render never writes over what is being typed.
    private HTMLInputElement<string> Segment(string kind, string label, string placeholder)
    {
        return Input.Of<string>()
            .Key(kind)
            .Type(InputType.Text)
            .Placeholder(placeholder)
            .Disabled(Disabled == true)
            .Class(UiTimePickerLook.Segment)
            .Data(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-" + kind + "-input"] = null, ["rask-segment"] = kind })
            .Aria("label", label);
    }

    // The hour, which the label points at, also says what the field says about the control.
    private static Dictionary<string, string?> Named(View view, string label) =>
        new(view.Field.Aria, StringComparer.Ordinal) { ["label"] = label };

    // The one field the parts are read and written through: the chosen time, or nothing.
    private HTMLInputElement<string> Whole(View view) =>
        Input
            .Value(view.Chosen.Count > 0 ? view.Chosen[0].ToString(Clock, CultureInfo.InvariantCulture) : string.Empty)
            .OnChange(text => TypedAsync(view, text))
            .Key("whole")
            .Type(InputType.Hidden);

    private Task TypedAsync(View view, string text)
    {
        if (!TimeOnly.TryParseExact(text, Clock, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return Task.CompletedTask;
        }

        return Off(time) ? Task.CompletedTask : CommitAsync(view, [time]);
    }
}
