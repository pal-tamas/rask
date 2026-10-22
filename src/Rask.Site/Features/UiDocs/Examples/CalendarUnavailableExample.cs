namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarUnavailableExample : Component
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);

    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).Unavailable([Today.AddDays(-1), Today.AddDays(1)]).Class("w-fit");
}
