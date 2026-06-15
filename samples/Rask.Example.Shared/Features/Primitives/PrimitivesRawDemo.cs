namespace Rask.Example.Shared.Features;

public sealed class PrimitivesRawDemo : Component
{
    protected override RenderResult Render() => P(Class: "mb-0")[Raw("Already <strong>safe</strong> HTML")];
}
