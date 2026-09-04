using Rask.Blazor;
using Rask.Example.Razor;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     The island. An empty body is the whole declaration: the chain steps —
///     <c>Symbol</c> (required, because the <c>.razor</c> marked it <c>[EditorRequired]</c>) and
///     <c>Price</c> — are read from <see cref="PriceTicker" />'s own <c>[Parameter]</c> properties.
/// </summary>
public sealed partial class PriceTickerIsland : BlazorComponent<PriceTicker>;

/// <summary>
///     A Blazor component rendered inside a Rask WASM page, running entirely in the browser.
/// </summary>
/// <remarks>
///     There is no Blazor circuit and no <c>blazor.web.js</c> here: the hosted component is rendered to
///     markup in-process by the WASM runtime, and its own <c>@onclick</c> / <c>@bind</c> travel the same
///     handler channel every Rask event uses.
/// </remarks>
public sealed partial class BlazorIslandDemo : Component
{
    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                PriceTickerIsland.Symbol("RASK").Price(12.50m)
            ]
        ];
}
