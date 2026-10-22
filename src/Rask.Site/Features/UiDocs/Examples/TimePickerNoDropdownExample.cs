namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerNoDropdownExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).Dropdown(false).Id("ui-time-typed")
        ];
}
