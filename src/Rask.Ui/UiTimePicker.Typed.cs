using System.Globalization;
using Rask.Core.Forms;

namespace Rask;

// The typed trigger: hour, minute and AM/PM fields in the input's box, and the list beside them.
public sealed partial class UiTimePicker<T>
{
    private const string Clock = "HH:mm";

    // The three fields are typed as ONE: the runtime (`data-rask-segments`) takes the digits, moves on from a
    // part that can take no more, steps a part on the vertical arrows and sets AM or PM on `a` and `p`, as
    // Flux's fields do. A whole time arrives through the one hidden field, as 24-hour `HH:mm`.
    private Component TypedTrigger(UiWithField field, ExpressionAccessor.Accessor? accessor, EditContext? context, List<TimeOnly> chosen)
    {
        var twelve = UiTimePickerLook.TwelveHour(Format, Culture);
        var opens = Dropdown != false && Disabled != true;
        var hour = RaskStrings.Get(RaskString.PickerHour, "Hour");

        var box = Div
            .Class(UiClass.Compose(UiTimePickerLook.InputBox, UiTimePickerLook.BoxSize(Size ?? Ui.TimePickerSize.Base)))
            .Data(field.Invalid ? UiTimePickerLook.InvalidBoxMarks : UiTimePickerLook.BoxMarks);

        // Everything in the box but a field opens the list, and a second press closes it, as on Flux: the
        // runtime's toggle, which leaves a press in a field to the field.
        box = opens
            ? box.Attributes(("data-rask-toggle", ListId), ("style", "anchor-name:--" + ListId))
            : box.Attributes(("style", "anchor-name:--" + ListId));

        return box[
            Ui.Icon.Name(Ui.IconName.Clock).Mini.Class(UiTimePickerLook.InputIcon),
            Div.Class(UiTimePickerLook.Segments).Data(UiTimePickerLook.SegmentsMark)
                .Attributes([("dir", "ltr"), .. UiFormCommit.FirstEdit(accessor, context)])[
                // Flux keeps a space's width between the minute and AM/PM (measured 2026-10-22).
                UiTimeParts.Of(twelve, Culture, Disabled == true, first => first.Id(field.ControlId).Aria(Named(field, hour)), space: " "),
                Whole(chosen)
            ],
            Span.Class("grow"),
            Dropdown != false ? Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiTimePickerLook.InputChevron) : null
        ];
    }

    // The hour, which the label points at, also says what the field says about the control.
    private static Dictionary<string, string?> Named(UiWithField field, string label) =>
        new(field.Aria, StringComparer.Ordinal) { ["label"] = label };

    // The one field the parts are read and written through: the chosen time, or nothing. Bound and waiting, it
    // is the browser's to keep until the next action, which it goes ahead of — as a bound input's value does —
    // so a time typed asks the page nothing until then.
    private HTMLInputElement<string> Whole(List<TimeOnly> chosen)
    {
        var whole = Input
            .Value(chosen.Count > 0 ? chosen[0].ToString(Clock, CultureInfo.InvariantCulture) : string.Empty)
            .OnChange(TypedAsync)
            .Key("whole")
            .Type(InputType.Hidden);
        return UiDraws.Waits(this, Bind is not null) ? whole.Attributes(UiTimePickerLook.WaitsForAction) : whole;
    }

    private Task TypedAsync(string text)
    {
        var bound = Latest();
        if (!TimeOnly.TryParseExact(text, Clock, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            // No time: the parts were left half filled, which the frame says beside its empty value, or emptied —
            // nothing chosen, where the bound type can say so.
            return UnreadInput.Current is null && UiTimePickerValue.Clears<T>()
                ? CommitAsync(bound, [])
                : UiFormCommit.UnreadAsync(this, bound.Accessor, bound.Context, Current(bound), RaskString.ValidationTime);
        }

        return Off(time) ? Task.CompletedTask : CommitAsync(bound, [time]);
    }

    private static T Current(Bound bound) => UiTimePickerValue.Single<T>(bound.Chosen.Count > 0 ? bound.Chosen[0] : null);
}
