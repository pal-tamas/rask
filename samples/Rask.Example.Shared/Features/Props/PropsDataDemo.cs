namespace Rask.Example.Shared.Features;

public sealed partial class PropsDataDemo : Component
{
    protected override Component? Render() =>
        Div
            .Class("p-2 bg-slate-100 rounded border")
            .Data(new Dictionary<string, string?> { ["role"] = "card", ["index"] = "7", ["new"] = null })[
            "Inspect the rendered HTML — data-role, data-index, and a bare data-new."];
}
