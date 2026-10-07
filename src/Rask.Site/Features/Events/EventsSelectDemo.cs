namespace Rask.Site.Features;

public sealed partial class EventsSelectDemo : Component
{
    private static readonly (string Value, string Text)[] Frameworks =
        [("rask", "Rask"), ("blazor", "Blazor"), ("htmx", "htmx")];

    private string _pick = "rask";

    protected override Component? Render() =>
    [
        Ui.Select.Value(_pick)
            .Label("Framework")
            .OnChange(v => _pick = v)
            .Class("mb-2")[
            Frameworks.Select(framework => Ui.SelectOption.Key(framework.Value).Value(framework.Value)[framework.Text])
        ],
        P.Class("text-sm mb-0")["Picked: ", Strong[_pick]]
    ];
}
