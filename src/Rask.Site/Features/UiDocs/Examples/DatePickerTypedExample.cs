using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

// Typed in the culture's own order — Hungarian here: 2026. 11. 06. — or picked, the header choosing month and year.
public sealed partial class DatePickerTypedExample : Component
{
    private DateOnly _date;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatePicker.Value(_date).OnChange(date => { _date = date; }).Typed().Locale("hu-HU"),
            Ui.Text.Data("testid", "ui-date-picker-typed-state")[
                _date == default ? "No date chosen." : "Chosen: " + _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ]
        ];
}
