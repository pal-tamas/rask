namespace Rask.Example.Shared.Features;

// The live-ticker lifecycle demo embedded in docs/lifecycle.md. Wraps the reusable LiveTicker widget
// (all the lifecycle hooks + the zero-JS server-rendered SVG chart) with a symbol switcher and a
// hook-activity log. The standalone /realtime/{Symbol} page drove Symbol from a [RouteParam] and switched
// by URL navigation; a co-mounted guide demo can't own a live route param, so the switcher flips Symbol via
// internal state — re-rendering reconciles the same LiveTicker instance at its stable tree position with the
// new Symbol, firing OnPropsChanged ("Symbol BTC → ETH") exactly as the route-param switch did.
public sealed class LiveTickerDemo : Component
{
    private readonly List<string> _log = new();
    private string _symbol = "BTC";

    protected override Component? Render() =>
    [
        Div(Class: "btn-group mb-3", Id: "ticker-symbol-switcher")[
            SwitchButton("BTC"),
            SwitchButton("ETH"),
            SwitchButton("SOL")
        ],
        Div(Class: "row g-4")[
            Div(Class: "col-lg-7")[
                LiveTicker(_symbol, Log: AppendLog)
            ],
            Div(Class: "col-lg-5")[
                BsCard(Class: "border-0 bg-light h-100")[
                    BsCardBody()[
                        Div(Class: "d-flex justify-content-between align-items-baseline mb-3")[
                            H3(Class: "h6 text-secondary text-uppercase small mb-0")["Hook activity"],
                            BsButton(Size: BsSize.Sm, Class: "btn-link p-0 text-decoration-none",
                                Id: "ticker-clear-log", OnClick: ClearLog)["clear"]
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
        ]
    ];

    // Internal-state switch (no URL navigation): mutating _symbol re-renders this demo, and the framework
    // auto-re-render on the click hands LiveTicker its new Symbol at the same position → OnPropsChanged.
    private Component SwitchButton(string symbol) =>
        Button(
            Class: _symbol == symbol
                ? "btn btn-primary btn-sm"
                : "btn btn-outline-primary btn-sm",
            Id: $"ticker-switch-{symbol}",
            OnClick: () => _symbol = symbol)[symbol];

    // The child LiveTicker logs from its lifecycle hooks / poll loop (off the event-dispatch path), so the
    // parent must request its own render — same DeferredRerenderAsync trick the standalone page used.
    private void AppendLog(string entry)
    {
        _log.Add($"{DateTime.UtcNow:HH:mm:ss.fff}  {entry}");
        if (_log.Count > 100)
        {
            _log.RemoveAt(0);
        }

        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    private void ClearLog() => _log.Clear();

    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
