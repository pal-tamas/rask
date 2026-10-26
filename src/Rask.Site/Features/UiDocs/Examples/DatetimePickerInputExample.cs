namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerInputExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.DatetimePicker.Value(default(DateTime)).Time(Ui.DatetimePickerTime.Input),
            Ui.DatetimePicker.Range.Value(default(UiDateTimeRange)).Time(Ui.DatetimePickerTime.Input)
        ];
}
