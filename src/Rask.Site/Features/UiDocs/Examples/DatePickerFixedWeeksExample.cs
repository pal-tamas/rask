namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerFixedWeeksExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatePicker.Value(default(DateOnly)).FixedWeeks()
        ];
}
