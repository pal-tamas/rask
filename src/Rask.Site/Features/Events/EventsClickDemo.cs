namespace Rask.Site.Features;

public sealed partial class EventsClickDemo : Component
{
    private int _clicks;

    protected override Component? Render() =>
        Ui.Button.Primary.OnClick(() => _clicks++)[Ui.Icon.Name(Ui.IconName.CursorArrowRays), $"Clicks: {_clicks}"];
}
