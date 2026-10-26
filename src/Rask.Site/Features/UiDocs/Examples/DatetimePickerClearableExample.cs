namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerClearableExample : Component
{
    private DateTime? _at = TimeProvider.System.GetLocalNow().Date.AddDays(3).AddHours(9);

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.DatetimePicker.Value(_at ?? default).OnChange(at => { _at = at == default ? null : at; }).Clearable(),
            Ui.Text.Data("testid", "ui-datetime-picker-clear-state")[_at is null ? "Nothing chosen." : "A moment is chosen."]
        ];
}
