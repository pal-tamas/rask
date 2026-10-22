namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarForceOpenToExample : Component
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);

    private static readonly DateOnly NextYear = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddYears(1);

    protected override Component? Render() =>
        Ui.Calendar.Value(Today).OpenTo(NextYear).ForceOpenTo().Class("w-fit");
}
