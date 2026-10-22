namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerUnavailableExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.TimePicker.Of<TimeOnly?>().Id("ui-time-unavailable").Unavailable([
                new TimeOnly(3, 0), new TimeOnly(4, 0), new UiTimeRange(new TimeOnly(5, 30), new TimeOnly(7, 29)),
            ])
        ];
}
