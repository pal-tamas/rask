namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarOpenToExample : Component
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);

    // The first of next month, a year on.
    private static readonly DateOnly NextYear = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddYears(1);

    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).OpenTo(NextYear).Class("w-fit");
}
