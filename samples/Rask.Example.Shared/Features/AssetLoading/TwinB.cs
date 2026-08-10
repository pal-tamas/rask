namespace Rask.Example.Shared.Features;

/// <summary>
///     Twin B — paired with <see cref="TwinA" />. Different content → different hash →
///     different URL. Demonstrates the per-component delivery isolation.
/// </summary>
public sealed partial class TwinB : Component
{
    protected override Component? Render() =>
        Div.Class("twin-tag")["Twin B — different colors, different hash"];
}
