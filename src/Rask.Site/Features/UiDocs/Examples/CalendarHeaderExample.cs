namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarHeaderExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).SelectableHeader().Class("w-fit");
}
