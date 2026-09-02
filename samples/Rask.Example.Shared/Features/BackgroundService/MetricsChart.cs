namespace Rask.Example.Shared.Features;

// The second of two independent subscribers to the same IMetricsFeed singleton, to make
// the "one background producer, many decoupled consumers" story concrete: the component
// subscribes to feed.Updated on mount, unsubscribes on unmount, and repaints itself
// straight from the latest snapshot. It knows nothing about MetricsGauge — both are
// driven by the background loop in MetricsFeed.
//
// The subscribe/unsubscribe pair mirrors ShowcaseLayout's route.Changed handling
// (ShowcaseLayout.cs:57-59): += in OnMount, -= in OnUnmount so a navigated-away
// component stops repainting and can be collected (no event-handler leak). Updated
// fires from a background thread; StateHasChanged() is thread-safe and a no-op once
// unmounted, so no extra marshalling is needed here (same as the disposable timer probe).

// SVG sparkline of the CPU history — a second, independent subscriber. Reuses the
// stateless Sparkline demo (zero JS, server-rendered SVG); the feed's rolling buffer is
// the data series. ValueFormat renders the axis labels as percentages instead of money.
public sealed partial class MetricsChart(IMetricsFeed feed) : Component
{
    protected override void OnMount() => feed.Updated += StateHasChanged;

    protected override void OnUnmount() => feed.Updated -= StateHasChanged;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0 h-full")[
            Div.Class(Tw.CardBody)[
                H3.Class("text-base font-semibold text-slate-500 dark:text-slate-400 uppercase text-sm mb-3")["CPU %, last minute"],
                Div
                    .Class("metrics-chart-container")
                    .Id("metrics-chart")
                    .Style("position: relative; height: 160px;")[
                    Sparkline
                        .Values(feed.State.Recent.Select(p => p.CpuPercent).ToList())
                        .ValueFormat("0.0'%'")
                        .Class("metrics-chart-svg")
                ]
            ]
        ];
}
