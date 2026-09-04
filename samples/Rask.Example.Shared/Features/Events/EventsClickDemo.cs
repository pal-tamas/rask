namespace Rask.Example.Shared.Features;

public sealed partial class EventsClickDemo : Component
{
    private int _clicks;

    protected override Component? Render() =>
        Button.Type("button").Class(Tw.BtnPrimary).OnClick(() => _clicks++)[UiIcon.Name(UiIconName.Cursor).Class("me-2"), $"Clicks: {_clicks}"];
}
