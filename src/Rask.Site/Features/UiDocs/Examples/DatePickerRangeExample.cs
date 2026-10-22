using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerRangeExample : Component
{
    private UiDateRange _range;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatePicker.Range.Value(_range).OnChange(range => { _range = range; }),
            Ui.Text.Data("testid", "ui-date-picker-range-state")[
                _range == default ? "No range chosen." : $"Range: {Iso(_range.Start)} to {Iso(_range.End)}"
            ]
        ];

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
