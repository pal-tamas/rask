namespace Rask.Site.Features;

public sealed partial class EventsClickDemo : Component
{
    private int _clicks;

    protected override Component? Render() =>
        UiButton.Tone(UiTone.Primary).OnClick(() => _clicks++)[UiIcon.Name(UiIconName.Cursor), $"Clicks: {_clicks}"];
}
