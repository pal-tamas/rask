namespace Rask.Example.Shared.Features;

public sealed partial class PrimitivesRawDemo : Component
{
    protected override Component? Render() => P.Class("mb-0")[Raw.Value("Already <strong>safe</strong> HTML")];
}
