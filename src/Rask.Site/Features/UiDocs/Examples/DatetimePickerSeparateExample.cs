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
        Div.Class("flex max-sm:flex-col gap-2 w-full max-w-64 sm:max-w-96")[
            Ui.DatePicker.Bind(() => _meeting.Day).Class("flex-1"),
            Ui.TimePicker.Bind(() => _meeting.At).Class("sm:w-40")
        ];
}
