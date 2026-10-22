namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarTodayExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).WithToday().Class("w-fit");
}
