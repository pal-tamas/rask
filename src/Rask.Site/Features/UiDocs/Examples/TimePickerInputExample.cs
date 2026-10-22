namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerInputExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).Id("ui-time-input")
        ];
}
