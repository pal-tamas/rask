namespace Rask.Example.Shared.Features;

public sealed class EventsSelectDemo : Component
{
    private string _pick = "rask";

    protected override RenderResult Render() =>
    [
        Select(
            Class: "form-select mb-2",
            OnChange: v => _pick = v)[
            Option("rask", _pick == "rask")["Rask"],
            Option("blazor", _pick == "blazor")["Blazor"],
            Option("htmx", _pick == "htmx")["htmx"]
        ],
        P(Class: "small mb-0")["Picked: ", Strong()[_pick]]
    ];
}
