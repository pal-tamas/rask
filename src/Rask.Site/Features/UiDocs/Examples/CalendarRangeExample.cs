using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarRangeExample : Component
{
    private UiDateRange _range;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Calendar.Range.Value(_range).OnChange(range => { _range = range; }).Class("w-fit"),
            Ui.Text.Data("testid", "ui-calendar-stay")[
                _range == default ? "No range chosen." : $"Range: {Iso(_range.Start)} to {Iso(_range.End)}"
            ]
        ];

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
