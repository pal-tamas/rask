namespace Rask.Example.Shared.Features;

public sealed class PrimitivesTextDemo : Component
{
    protected override RenderResult Render() => P(Class: "mb-0")["1 < 2 && \"safe\""];
}
