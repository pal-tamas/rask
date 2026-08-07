namespace Rask.Example.Shared.Features;

public sealed partial class PrimitivesChildrenDemo : Component
{
    protected override Component? Render() => Div(Class: "mb-0")[
        "plain text, ",
        Strong()["bold text, "],
        $"interpolated: {DateTime.Today:yyyy-MM-dd}"
    ];
}
