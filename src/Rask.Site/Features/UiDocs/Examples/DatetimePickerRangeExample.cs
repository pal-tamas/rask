using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerRangeExample : Component
{
    private UiDateTimeRange _schedule;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatetimePicker.Range.Value(_schedule).OnChange(range => { _schedule = range; }),
            Ui.Text.Data("testid", "ui-datetime-picker-range-state")[_schedule == default ? "No window chosen." : "From " + At(_schedule.Start) + " to " + At(_schedule.End)]
        ];

    private static string At(DateTime at) => at.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
}
