using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerInputExample : Component
{
    private DateOnly _date;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatePicker.Value(_date).OnChange(date => { _date = date; }).Type(Ui.DatePickerType.Input),
            Ui.Text.Data("testid", "ui-date-picker-input-state")[
                _date == default ? "No date chosen." : "Chosen: " + _date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            ]
        ];
}
