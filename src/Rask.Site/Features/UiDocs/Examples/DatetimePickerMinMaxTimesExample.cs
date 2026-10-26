namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerMinMaxTimesExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatetimePicker.Value(default(DateTime)).Min(TimeProvider.System.GetLocalNow().DateTime).MinTime(new TimeOnly(9, 0)).MaxTime(new TimeOnly(17, 0))
        ];
}
