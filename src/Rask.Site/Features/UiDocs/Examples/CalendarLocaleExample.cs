namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalendarLocaleExample : Component
{
    protected override Component? Render() =>
        Ui.Calendar.Value(default(DateOnly)).Locale("ja-JP").Class("w-fit");
}
