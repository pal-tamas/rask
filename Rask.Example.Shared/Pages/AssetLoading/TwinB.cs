namespace Rask.Example.Shared.Pages.AssetLoading;

/// <summary>
///     Twin B — paired with <see cref="TwinA" />. Different content → different hash →
///     different URL. Demonstrates the per-component delivery isolation.
/// </summary>
public sealed class TwinB : Component
{
    protected override RenderResult Render() =>
        Div(Class: "twin-tag")["Twin B — different colors, different hash"];
}
