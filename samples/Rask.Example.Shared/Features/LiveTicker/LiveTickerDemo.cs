namespace Rask.Example.Shared.Features;

// The live-ticker lifecycle demo embedded in docs/lifecycle.md. Wraps the reusable LiveTicker widget
// (all the lifecycle hooks + the zero-JS server-rendered SVG chart) with a symbol switcher and a
// hook-activity log. The standalone /realtime/{Symbol} page drove Symbol from a [RouteParam] and switched
// by URL navigation; a co-mounted guide demo can't own a live route param, so the switcher flips Symbol via
// internal state — re-rendering reconciles the same LiveTicker instance at its stable tree position with the
// new Symbol, firing OnPropsChanged ("Symbol BTC → ETH") exactly as the route-param switch did.
public sealed partial class LiveTickerDemo : Component
{
    private readonly List<string> _log = new();
    private string _symbol = "BTC";

    protected override Component? Render() =>
    [
        Div.Class("mb-3").Id("ticker-symbol-switcher")[
            SwitchButton("BTC"),
            SwitchButton("ETH"),
            SwitchButton("SOL")
        ],
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("lg:col-span-7")[
                LiveTicker.Symbol(_symbol).Log(AppendLog)
            ],
            Div.Class("lg:col-span-5")[
                Div.Class($"{Tw.Card} border-0 bg-slate-100 h-full")[
                    Div.Class(Tw.CardBody)[
                        Div.Class("mb-3 flex flex-wrap items-center justify-between")[
                            H3.Class("text-base font-semibold text-slate-500 dark:text-slate-400 uppercase text-sm mb-0")["Hook activity"],
                            Button.Class(Tw.BtnLink).Type("button")
                                .Id("ticker-clear-log")
                                .OnClick(ClearLog)["clear"]
                        ],
                        _log.Count == 0
                            ? P.Class("text-slate-500 dark:text-slate-400 italic text-sm mb-0")[
                                "Empty — hooks will fire as the component mounts and ticks."]
                            : (Component)Ol
                                .Class($"{Tw.ListGroup} list-decimal list-inside divide-y divide-slate-200 dark:divide-slate-700")
                                .Id("ticker-log")
                                .Style("max-height: 360px; overflow-y: auto;")[
                                _log.Select((l, i) => Li
                                    .Key(i)
                                    .Class($"{Tw.ListGroupItem} ps-2 text-sm bg-transparent")[
                                    Code.Class("text-sm")[l]]).ToArray()]
                    ]
                ]
            ]
        ]
    ];

    // Internal-state switch (no URL navigation): mutating _symbol re-renders this demo, and the framework
    // auto-re-render on the click hands LiveTicker its new Symbol at the same position → OnPropsChanged.
    private Component SwitchButton(string symbol) =>
        Button
            .Class(_symbol == symbol
                ? $"{Tw.BtnPrimary}"
                : $"{Tw.BtnOutlinePrimary}")
            .Id($"ticker-switch-{symbol}")
            .OnClick(() => _symbol = symbol)[symbol];

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
