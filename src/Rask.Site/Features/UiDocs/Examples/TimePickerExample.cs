using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerExample : Component
{
    private TimeOnly? _time;

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.TimePicker.Value(_time).OnChange(time => { _time = time; }).Id("ui-time"),
            Ui.Text.Data("testid", "ui-time-picker-state")[
                _time is { } at ? "Chosen: " + at.ToString("HH:mm", CultureInfo.InvariantCulture) : "No time chosen."
            ]
        ];
}
