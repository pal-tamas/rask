namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerMinMaxExample : Component
{
    private readonly DateTime _now = TimeProvider.System.GetLocalNow().DateTime;

    protected override Component? Render() =>
        Div.Class("max-w-xs")[Ui.DatetimePicker.Value(default(DateTime)).Min(_now).Max(_now.Date.AddDays(14).AddHours(17))];
}
