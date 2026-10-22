namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarFixedWeeksExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).FixedWeeks().Class("w-fit");
}
