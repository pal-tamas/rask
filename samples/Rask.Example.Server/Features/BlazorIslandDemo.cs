using Rask.Blazor;
using Rask.Example.Razor;
using Rask.Example.Shared;

namespace Rask.Example.Server.Features;

/// <summary>The ticker island — the same one the WASM showcase hosts, here on the Server host.</summary>
public sealed partial class ServerPriceTickerIsland : BlazorComponent<PriceTicker>;

/// <summary>
///     The island that reads a browser API from <c>OnAfterRenderAsync</c>.
/// </summary>
/// <remarks>
///     This one is the point of the page. It is what #956 unblocked and what nothing gated before:
///     the hosted component injects <c>IJSRuntime</c>, which only arrives because the island now
///     builds through Blazor's own activator, and calls it from a hook <c>StaticHtmlRenderer</c> never
///     fires and Rask now drives.
/// </remarks>
public sealed partial class ViewportProbeIsland : BlazorComponent<ViewportProbe>;

/// <summary>
///     Two Blazor components rendered inside a Rask SERVER page — one interactive, one reading the
///     browser.
/// </summary>
/// <remarks>
///     The Server host had no Blazor sample at all until now, and that absence is exactly why a
///     hosted component's <c>[Inject]</c> could be null for a whole release without anyone noticing:
///     every gate the lane had ran against the WASM showcase or a unit test, and neither exercises the
///     path where the browser answer has to come back over a WebSocket.
/// </remarks>
public sealed partial class BlazorIslandDemo : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-col gap-4")[
            Div.Class($"{Tw.Card} shadow-sm border-0")[
                Div.Class(Tw.CardBody)[
                    ServerPriceTickerIsland.Symbol("RASK").Price(12.50m)
                ]
            ],
            Div.Class($"{Tw.Card} shadow-sm border-0")[
                Div.Class(Tw.CardBody)[
                    ViewportProbeIsland
                ]
            ]
        ];
}
