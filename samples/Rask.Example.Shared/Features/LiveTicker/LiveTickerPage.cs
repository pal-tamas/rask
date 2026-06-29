using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("realtime/{Symbol}")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class LiveTickerPage(Navigator nav) : Component
{
    private readonly List<string> _log = new();

    [RouteParam] public string Symbol { get; set; } = "BTC";

    protected override RenderResult Head => Title()[$"{Symbol} live ticker — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            $"{Symbol} live ticker",
            "A widget that exercises the lifecycle hooks. The Symbol comes from the [RouteParam] in the URL; switching it flips the route param and exercises OnPropsChanged. The poll loop in OnMountAsync drifts a synthetic price each tick (so the demo is deterministic and offline-safe). The chart is a server-rendered SVG drawn straight from the rolling buffer — no Chart.js, no canvas, and no JavaScript at all."),
        Div(Class: "btn-group mb-3", Id: "ticker-symbol-switcher")[
            SwitchButton("BTC"),
            SwitchButton("ETH"),
            SwitchButton("SOL")
        ],
        Div(Class: "row g-4")[
            Div(Class: "col-lg-7")[
                LiveTicker(Symbol, Log: AppendLog)
            ],
            Div(Class: "col-lg-5")[
                BsCard(Class: "border-0 bg-light h-100")[
                    BsCardBody()[
                        Div(Class: "d-flex justify-content-between align-items-baseline mb-3")[
                            H3(Class: "h6 text-secondary text-uppercase small mb-0")["Hook activity"],
                            BsButton(Size: BsSize.Sm, Class: "btn-link p-0 text-decoration-none", Id: "ticker-clear-log", OnClick: ClearLog)["clear"]
                        ],
                        _log.Count == 0
                            ? P(Class: "text-secondary fst-italic small mb-0")[
                                "Empty — hooks will fire as the component mounts and ticks."]
                            : (Component)Ol(
                                Class: "list-group list-group-numbered list-group-flush",
                                Id: "ticker-log",
                                Style: "max-height: 360px; overflow-y: auto;")[
                                _log.Select((l, i) => Li(Key: i,
                                    Class: "list-group-item ps-2 small bg-transparent")[
                                    Code(Class: "small")[l]]).ToArray()]
                    ]
                ]
            ]
        ],
        H2(Class: "h4 mt-5 mb-3")["What each hook does here"],
        P(Class: "text-secondary")[
            "Every override in ", Code()["LiveTicker"], " has a natural production job. Click ",
            Code()["ETH"], " or ", Code()["SOL"],
            " above to watch the OnPropsChanged + OnPropsChangedAsync entries fire, then click ",
            A("/lifecycle", "_self", Class: "")["Lifecycle"], " in the sidebar to navigate away and see ",
            Code()["OnUnmount"], " + ", Code()["OnUnmountAsync"],
            " (the log lives on the page, so unmount entries survive)."
        ],
        CodeSample(
            ["LiveTicker.cs"],
            Notes:
            "OnMountAsync runs a long-lived poll loop. Every await uses ConfigureAwait(false) so the loop doesn't auto-render on each yield; instead it calls StateHasChanged() once per real data change — one render per tick. The inter-tick delay is interruptible: a Symbol switch cancels _wake so the new asset polls immediately. CancellationToken cancels on unmount, breaking the loop. There is no OnRenderedAsync: the chart is a server-rendered SVG (the Sparkline component) emitted straight from Render(), so the framework ships the updated <svg> over the same transport as the rest of the page — zero JavaScript."),
        BsAlert(Color: BsColor.Info, Class: "d-flex align-items-start mt-3")[
            I(Class: "bi bi-info-circle-fill me-3 fs-4"),
            Div()[
                Strong()["Synthetic feed."],
                " The poll loop generates a random-walk price locally (with a 50 ms ",
                Code()["Task.Delay"], " to simulate latency) so the demo is offline-safe. ",
                "Switching to a real HTTP source is a one-line change in ",
                Code()["PollOnceAsync"], "; the rest of the component — lifecycle, ",
                "cancellation, the SVG chart — is identical."
            ]
        ]
    ];

    private Component SwitchButton(string symbol) =>
        Button(
            Class: Symbol == symbol
                ? "btn btn-primary btn-sm"
                : "btn btn-outline-primary btn-sm",
            Id: $"ticker-switch-{symbol}",
            OnClick: () => nav.NavigateTo($"/realtime/{symbol}"))[symbol];

    private void AppendLog(string entry)
    {
        _log.Add($"{DateTime.UtcNow:HH:mm:ss.fff}  {entry}");
        // Cap parent log too so we don't grow unbounded — keep the last 100.
        if (_log.Count > 100)
        {
            _log.RemoveAt(0);
        }

        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    private void ClearLog() => _log.Clear();

    // Same trick LifecyclePage uses: an unmount-time StateHasChanged lands in
    // the in-handler guard and gets dropped on WASM. Yielding to the event loop
    // lets the lock release before we request the follow-up render.
    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
