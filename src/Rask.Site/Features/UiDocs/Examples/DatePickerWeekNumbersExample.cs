namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerWeekNumbersExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatePicker.Value(default(DateOnly)).WeekNumbers()
        ];
}
