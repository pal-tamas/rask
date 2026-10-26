namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerDefaultTimeExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.DatetimePicker.Value(default(DateTime)).DefaultTime(new TimeOnly(9, 0)),
            Ui.DatetimePicker.Range.Value(default(UiDateTimeRange)).DefaultTime(new UiTimeRange(new TimeOnly(9, 0), new TimeOnly(17, 0)))
        ];
}
