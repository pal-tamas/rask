namespace Rask.Example.Shared.Features;

public sealed partial class PrimitivesFragmentDemo : Component
{
    protected override Component? Render() => [
        H3.Class("h5")["A heading"],
        P.Class("mb-0")["A paragraph"]
    ];
}
