namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerOpenToExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.TimePicker.Of<TimeOnly?>().OpenTo(new TimeOnly(10, 0)).Id("ui-time-open")
        ];
}
