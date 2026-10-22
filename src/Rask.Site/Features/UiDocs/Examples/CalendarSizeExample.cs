namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarSizeExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).Xl.Class("w-fit");
}
