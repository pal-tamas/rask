namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerTriggerExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatetimePicker.Value(default(DateTime)).Trigger(Ui.Button.Icon(Ui.IconName.CalendarDays)["Schedule"])
        ];
}
