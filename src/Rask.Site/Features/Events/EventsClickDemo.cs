namespace Rask.Site.Features;

public sealed partial class EventsClickDemo : Component
{
    private int _clicks;

    protected override Component? Render() =>
        Ui.Button.Tone(Ui.Tone.Primary).OnClick(() => _clicks++)[Ui.Icon.Name(Ui.IconName.Cursor), $"Clicks: {_clicks}"];
}
