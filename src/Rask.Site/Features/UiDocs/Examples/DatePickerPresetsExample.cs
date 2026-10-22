using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerPresetsExample : Component
{
    private UiDateRange _range;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            // Min is where "All Time" starts.
            Ui.DatePicker.Range.Value(_range).OnChange(range => { _range = range; }).WithPresets().Min(new DateOnly(2012, 1, 1)),
            Ui.Text.Data("testid", "ui-date-picker-presets-state")[_range == default ? "No range chosen." : "Range: " + Named(_range)]
        ];

    private static string Named(UiDateRange range) =>
        range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " to " + range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
