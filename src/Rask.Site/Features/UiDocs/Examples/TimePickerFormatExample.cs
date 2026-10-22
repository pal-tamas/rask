namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerFormatExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.TimePicker.Of<TimeOnly?>().TwelveHour.Id("ui-time-twelve"),
            Ui.TimePicker.Of<TimeOnly?>().TwentyFourHour.Id("ui-time-twenty-four")
        ];
}
