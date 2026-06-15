namespace Rask.Example.Shared.Features;

public sealed class PrimitivesFragmentDemo : Component
{
    protected override RenderResult Render() => Fragment()[
        H3(Class: "h5")["A heading"],
        P(Class: "mb-0")["A paragraph"]
    ];
}
