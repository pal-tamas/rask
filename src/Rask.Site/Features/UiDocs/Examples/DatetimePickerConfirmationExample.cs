namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerConfirmationExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[Ui.DatetimePicker.Value(default(DateTime)).WithConfirmation()];
}
