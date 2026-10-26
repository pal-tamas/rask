namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerSeparateExample : Component
{
    private readonly Meeting _meeting = new();

    public sealed class Meeting
    {
        public DateOnly? Day { get; set; }

        public TimeOnly? At { get; set; }
    }

    protected override Component? Render() =>
        Div.Class("flex gap-2 max-w-md")[
            Ui.DatePicker.Bind(() => _meeting.Day).Class("flex-1"),
            Ui.TimePicker.Bind(() => _meeting.At).Class("w-40")
        ];
}
