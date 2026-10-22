namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarStaticExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date)).Static().Xs.Navigation(false).Class("w-fit");
}
