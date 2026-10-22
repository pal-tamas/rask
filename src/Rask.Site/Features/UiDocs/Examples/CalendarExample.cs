using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarExample : Component
{
    private DateOnly _date;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Calendar.Value(_date).OnChange(date => { _date = date; }).Class("w-fit"),
            Ui.Text.Data("testid", "ui-calendar-state")[
                _date == default ? "No date chosen." : "Chosen: " + _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ]
        ];
}
