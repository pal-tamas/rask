namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarStartDayExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).StartDay(DayOfWeek.Monday).Class("w-fit");
}
