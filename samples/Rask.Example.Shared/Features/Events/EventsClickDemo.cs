namespace Rask.Example.Shared.Features;

public sealed class EventsClickDemo : Component
{
    private int _clicks;

    protected override RenderResult Render() =>
        BsButton(Color: BsColor.Primary, OnClick: () => _clicks++)[BsIcon(Name: BsIconName.HandIndex, Class: "me-2"), $"Clicks: {_clicks}"];
}
