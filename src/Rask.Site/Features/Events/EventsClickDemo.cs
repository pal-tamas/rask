namespace Rask.Site.Features;

public sealed partial class EventsClickDemo : Component
{
    private int _clicks;

    protected override Component? Render() =>
        Ui.Button.Primary.Icon(Ui.IconName.CursorArrowRays).OnClick(() => _clicks++)[$"Clicks: {_clicks}"];
}
