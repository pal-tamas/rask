namespace Rask.Example.Shared.Features;

public sealed partial class EventsSelectDemo : Component
{
    private string _pick = "rask";

    protected override Component? Render() =>
    [
        Select.Value<string>(null)
            .Class($"{Tw.Select} mb-2")
            .OnChange(v => _pick = v)[
            Option.Value("rask").Selected(_pick == "rask")["Rask"],
            Option.Value("blazor").Selected(_pick == "blazor")["Blazor"],
            Option.Value("htmx").Selected(_pick == "htmx")["htmx"]
        ],
        P.Class("text-sm mb-0")["Picked: ", Strong[_pick]]
    ];
}
