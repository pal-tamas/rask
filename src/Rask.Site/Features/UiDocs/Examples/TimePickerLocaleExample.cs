namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class TimePickerLocaleExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.TimePicker.Of<TimeOnly?>().Locale("ja-JP").Id("ui-time-locale")
        ];
}
