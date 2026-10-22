namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerMultipleExample : Component
{
    private List<TimeOnly> _times = [];

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.TimePicker.Value(_times).OnChange(times => { _times = [.. times]; }).Id("ui-time-multiple"),
            Ui.Text.Data("testid", "ui-time-picker-slots")[$"{_times.Count} times"]
        ];
}
