using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerExample : Component
{
    private DateTime _at = TimeProvider.System.GetLocalNow().Date.AddDays(5).AddHours(10).AddMinutes(30);

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatetimePicker.Value(_at).OnChange(at => { _at = at; }),
            Ui.Text.Data("testid", "ui-datetime-picker-state")[_at == default ? "Nothing chosen." : "Chosen: " + _at.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)]
        ];
}
