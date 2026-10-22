namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerMinMaxExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.TimePicker.Of<TimeOnly?>().Min(new TimeOnly(9, 0)).Max(new TimeOnly(17, 0)).Id("ui-time-min-max")
        ];
}
