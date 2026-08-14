namespace Rask.Example.Shared.Features;

public sealed partial class PrimitivesTextDemo : Component
{
    protected override Component? Render() => P.Class("mb-0")["1 < 2 && \"safe\""];
}
