namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarMultipleExample : Component
{
    private List<DateOnly> _dates = [];

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Calendar.Multiple.Values(_dates).OnChange(dates => { _dates = [.. dates]; }).Class("w-fit"),
            Ui.Text.Data("testid", "ui-calendar-days")[$"{_dates.Count} days off"]
        ];
}
