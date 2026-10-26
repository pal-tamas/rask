namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerRangeLimitsExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[Ui.DatetimePicker.Range.Value(default(UiDateTimeRange)).MaxRange(7)];
}
