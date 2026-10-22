namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerIntervalExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.TimePicker.Of<TimeOnly?>().Interval(60).Id("ui-time-interval")
        ];
}
