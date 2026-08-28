namespace Rask.Example.Shared.Features;

public sealed partial class EventsClickDemo : Component
{
    private int _clicks;

    protected override Component? Render() =>
        Button.Type("button").Class(Ui.BtnPrimary).OnClick(() => _clicks++)[Icon.Name(IconName.HandIndex).Class("me-2"), $"Clicks: {_clicks}"];
}
