using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

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
                "A widget that exercises every lifecycle hook. The Symbol comes from the [RouteParam] in the URL; switching it flips the route param and exercises OnPropsChanged. The poll loop in OnMountAsync drifts a synthetic price each tick (so the demo is deterministic and offline-safe); the chart re-renders via OnRenderedAsync; sessionStorage keeps the history across navigations."),
            Div(Class: "btn-group mb-3", Id: "ticker-symbol-switcher")[
                SwitchButton("BTC"),
                SwitchButton("ETH"),
                SwitchButton("SOL")
            ],
            Div(Class: "row g-4")[
                Div(Class: "col-lg-7")[
                    LiveTicker(Symbol: Symbol, Log: AppendLog)
                ],
                Div(Class: "col-lg-5")[
                    Div(Class: "card border-0 bg-light h-100")[
                        Div(Class: "card-body")[
                            Div(Class: "d-flex justify-content-between align-items-baseline mb-3")[
                                H3(Class: "h6 text-secondary text-uppercase small mb-0")["Hook activity"],
                                Button(
                                    Class: "btn btn-sm btn-link p-0 text-decoration-none",
                                    Id: "ticker-clear-log",
                                    OnClick: ClearLog)["clear"]
                            ],
                            _log.Count == 0
                                ? P(Class: "text-secondary fst-italic small mb-0")[
                                    "Empty — hooks will fire as the component mounts and ticks."]
                                : (Component)Ol(
                                    Class: "list-group list-group-numbered list-group-flush",
                                    Id: "ticker-log",
                                    Style: "max-height: 360px; overflow-y: auto;")[
                                    _log.Select(l => (Child)Li(
                                        Class: "list-group-item ps-2 small bg-transparent")[
                                        Code(Class: "small")[l]]).ToArray()]
                        ]
                    ]
                ]
            ],
            H2(Class: "h4 mt-5 mb-3")["What each hook does here"],
            P(Class: "text-secondary")[
                "Every override in ", Code()["LiveTicker"], " has a natural production job. Click ",
                Code()["ETH"], " or ", Code()["SOL"], " above to watch the OnPropsChanged + OnPropsChangedAsync entries fire, then click ",
                A("/lifecycle", "_self", Class: "")["Lifecycle"], " in the sidebar to navigate away and see ",
                Code()["OnUnmount"], " + ", Code()["OnUnmountAsync"], " (the log lives on the page, so unmount entries survive)."
            ],
            CodeSample(
                """
                public sealed class LiveTicker(IJSRuntime js) : Component
                {
                    public string Symbol { get; set; } = "BTC";
                    public int Interval { get; set; } = 1000;

                    protected override RenderResult Head =>
                        Script(LiveOptions.PathBase + "/lib/chartjs/chart.umd.js");

                    protected override void OnMount() { /* record _mountedAt */ }

                    protected override async Task OnMountAsync()
                    {
                        await LoadFromStorageAsync().ConfigureAwait(false);
                        var ct = CancellationToken;   // cancels on unmount
                        try
                        {
                            while (!ct.IsCancellationRequested)
                            {
                                var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                Interlocked.Exchange(ref _wake, wake)?.Dispose();
                                await PollOnceAsync(ct).ConfigureAwait(false);  // synthetic feed → history
                                try { await Task.Delay(Interval, wake.Token).ConfigureAwait(false); }
                                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                            }
                        }
                        catch (OperationCanceledException) { /* unmount */ }
                    }

                    protected override void OnPropsChanged() { /* detect Symbol change */ }

                    protected override async Task OnPropsChangedAsync()
                    {
                        if (_lastSymbol is not null && _lastSymbol != Symbol)
                        {
                            _history.Clear();
                            await LoadFromStorageAsync(); // reseed from new symbol's history
                            _wake?.Cancel();              // poll the new symbol immediately
                        }
                        _lastSymbol = Symbol;
                    }

                    protected override void OnRendered(bool firstRender) { /* paint latency */ }

                    protected override async Task OnRenderedAsync(bool firstRender)
                    {
                        if (_version == _lastDrawnVersion) return;  // buffer unchanged → skip JS
                        _lastDrawnVersion = _version;
                        await js.InvokeVoidAsync("Rask.LiveTicker.draw", _history.ToArray());
                    }

                    protected override void OnUnmount() { /* sync log */ }

                    protected override async Task OnUnmountAsync() =>
                        await Task.Delay(50); // stand-in for POST /stats/session-end
                }
                """,
                Notes:
                "OnMountAsync runs a long-lived poll loop. Every await uses ConfigureAwait(false) so the loop doesn't auto-render on each yield; instead it calls StateHasChanged() once per real data change — one render and one chart redraw per tick. The inter-tick delay is interruptible: a Symbol switch cancels _wake so the new asset polls immediately. OnRenderedAsync is version-gated, so a no-op publish render skips the IJSRuntime round-trip. CancellationToken cancels on unmount, breaking the loop. Storage round-trips go through standard sessionStorage.getItem / setItem via IJSRuntime."),
            Div(Class: "alert alert-info d-flex align-items-start mt-3")[
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div()[
                    Strong()["Synthetic feed."],
                    " The poll loop generates a random-walk price locally (with a 50 ms ",
                    Code()["Task.Delay"], " to simulate latency) so the demo is offline-safe. ",
                    "Switching to a real HTTP source is a one-line change in ",
                    Code()["PollOnceAsync"], "; the rest of the component — lifecycle, ",
                    "cancellation, sessionStorage, chart redraws — is identical."
                ]
            ]
        ];

    private Component SwitchButton(string symbol) =>
        Button(
            Class: Symbol == symbol
                ? "btn btn-primary btn-sm"
                : "btn btn-outline-primary btn-sm",
            Id: $"ticker-switch-{symbol}",
            OnClick: () => nav.Navigate($"/realtime/{symbol}"))[symbol];

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

    private void ClearLog()
    {
        _log.Clear();
    }

    // Same trick LifecyclePage uses: an unmount-time StateHasChanged lands in
    // the in-handler guard and gets dropped on WASM. Yielding to the event loop
    // lets the lock release before we request the follow-up render.
    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
