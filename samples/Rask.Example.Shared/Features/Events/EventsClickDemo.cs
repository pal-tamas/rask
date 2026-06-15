namespace Rask.Example.Shared.Features;

public sealed class EventsClickDemo : Component
{
    private int _clicks;

    protected override RenderResult Render() =>
        Button(
            Class: "btn btn-primary",
            OnClick: () => _clicks++)[I(Class: "bi bi-hand-index me-2"), $"Clicks: {_clicks}"];
}
