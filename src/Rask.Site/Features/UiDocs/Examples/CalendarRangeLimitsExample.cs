namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarRangeLimitsExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Range.Value(default(UiDateRange)).MinRange(3).MaxRange(10).Class("w-fit");
}
