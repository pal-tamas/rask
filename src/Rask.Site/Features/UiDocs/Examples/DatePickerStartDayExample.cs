namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerStartDayExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatePicker.Value(default(DateOnly)).StartDay(DayOfWeek.Monday)
        ];
}
