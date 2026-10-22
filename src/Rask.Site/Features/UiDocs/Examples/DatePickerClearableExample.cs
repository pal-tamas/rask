namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class DatePickerClearableExample : Component
{
    private DateOnly _day = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);
    private TimeOnly? _time = new TimeOnly(9, 30);

    protected override Component? Render() =>
        Div.Class("max-w-md space-y-3")[
            Div.Class("flex flex-wrap gap-2").Data("testid", "ui-picker-clear")[
                Ui.DatePicker.Value(_day).OnChange(day => { _day = day; }).Clearable().Class("grow"),
                Ui.TimePicker.Value(_time).OnChange(time => { _time = time; }).Clearable().Id("ui-time-clear")
            ],
            Ui.Text.Data("testid", "ui-picker-clear-state")[(_day == default ? "No day" : "A day") + ", " + (_time is null ? "no time" : "a time")]
        ];
}
