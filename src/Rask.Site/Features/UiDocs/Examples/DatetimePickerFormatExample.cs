namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerFormatExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-4")[
            Ui.DatetimePicker.Value(default(DateTime)).Label("12-hour").TimeFormat(Ui.DatetimePickerTimeFormat.TwelveHour),
            Ui.DatetimePicker.Value(default(DateTime)).Label("24-hour").TimeFormat(Ui.DatetimePickerTimeFormat.TwentyFourHour)
        ];
}
