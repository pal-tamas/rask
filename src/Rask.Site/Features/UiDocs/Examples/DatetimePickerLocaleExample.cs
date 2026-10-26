namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerLocaleExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[Ui.DatetimePicker.Value(default(DateTime)).Locale("fr-FR")];
}
