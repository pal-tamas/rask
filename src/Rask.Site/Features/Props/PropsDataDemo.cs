namespace Rask.Site.Features;

public sealed partial class PropsDataDemo : Component
{
    protected override Component? Render() =>
        Div
            .Class("p-2 bg-zinc-50 dark:bg-zinc-900 rounded border")
            .Data(("role", "card"), ("index", "7"), ("new", null))[
            "Inspect the rendered HTML — data-role, data-index, and a bare data-new."];
}
