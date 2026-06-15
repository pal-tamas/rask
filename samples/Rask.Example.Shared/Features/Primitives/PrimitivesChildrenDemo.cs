namespace Rask.Example.Shared.Features;

public sealed class PrimitivesChildrenDemo : Component
{
    protected override RenderResult Render() => Div(Class: "mb-0")[
        "plain text, ",
        Strong()["bold text, "],
        $"interpolated: {DateTime.Today:yyyy-MM-dd}"
    ];
}
