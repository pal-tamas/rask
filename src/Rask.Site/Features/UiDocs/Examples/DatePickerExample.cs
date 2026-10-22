using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerExample : Component
{
    private DateOnly _date;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatePicker.Value(_date).OnChange(date => { _date = date; }),
            Ui.Text.Data("testid", "ui-date-picker-state")[_date == default ? "No date chosen." : "Chosen: " + Iso(_date)]
        ];

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
