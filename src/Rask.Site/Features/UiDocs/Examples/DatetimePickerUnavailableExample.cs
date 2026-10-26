namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatetimePickerUnavailableExample : Component
{
    private readonly DateOnly _today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);

    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.DatetimePicker.Value(default(DateTime)).Min(_today.ToDateTime(TimeOnly.MinValue)).MinTime(new TimeOnly(8, 0)).MaxTime(new TimeOnly(18, 0)).Unavailable([
                _today.AddDays(2),
                new UiTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0)),
                _today.AddDays(1).ToDateTime(new TimeOnly(10, 0)),
                UiDatetimeSlot.On(_today.AddDays(1), new TimeOnly(15, 0), new TimeOnly(16, 0)),
            ])
        ];
}
