namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarMaxExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).Max(DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date)).Class("w-fit");
}
