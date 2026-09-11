namespace Rask.Site.Features;

public sealed partial class EventsSelectDemo : Component
{
    private static readonly (string Value, string Text)[] Frameworks =
        [("rask", "Rask"), ("blazor", "Blazor"), ("htmx", "htmx")];

    private string _pick = "rask";

    protected override Component? Render() =>
    [
        UiSelect.Value(_pick)
            .Options(Frameworks)
            .AccessibleLabel("Framework")
            .OnChange(v => _pick = v)
            .Class("mb-2"),
        P.Class("text-sm mb-0")["Picked: ", Strong[_pick]]
    ];
}
