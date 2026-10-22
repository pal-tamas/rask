namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarWeekNumbersExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).WeekNumbers().Class("w-fit");
}
