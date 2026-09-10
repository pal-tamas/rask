namespace Rask.Site.Features;

public sealed partial class EventsClickDemo : Component
{
    private int _clicks;

    protected override Component? Render() =>
        UiButton.Label($"Clicks: {_clicks}").Icon(UiIconName.Cursor).Tone(UiTone.Primary).OnClick(() => _clicks++);
}
