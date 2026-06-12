using System.Globalization;

namespace Rask.Example.Shared.Demos;

// Two independent subscribers to the same IMetricsFeed singleton, to make the
// "one background producer, many decoupled consumers" story concrete: each component
// subscribes to feed.Updated on mount, unsubscribes on unmount, and repaints itself
// straight from the latest snapshot. Neither knows about the other — both are driven
// by the background loop in MetricsFeed.
//
// The subscribe/unsubscribe pair mirrors ShowcaseLayout's route.Changed handling
// (ShowcaseLayout.cs:57-59): += in OnMount, -= in OnUnmount so a navigated-away
// component stops repainting and can be collected (no event-handler leak). Updated
// fires from a background thread; StateHasChanged() is thread-safe and a no-op once
// unmounted, so no extra marshalling is needed here (same as DisposalPage's timer probe).

// Numeric readout of the latest tick. Ids are stable so the E2E journey can assert the
// tick count advances without any user interaction.
public sealed class MetricsGauge(IMetricsFeed feed) : Component
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    protected override void OnMount() => feed.Updated += StateHasChanged;

    protected override void OnUnmount() => feed.Updated -= StateHasChanged;

    protected override RenderResult Render()
    {
        var s = feed.State.Current;
        return Div(Class: "card shadow-sm border-0 h-100")[
            Div(Class: "card-body")[
                Div(Class: "d-flex align-items-baseline justify-content-between mb-3")[
                    H3(Class: "h6 text-secondary text-uppercase small mb-0")["System metrics"],
                    Span(Class: "badge rounded-pill text-bg-secondary", Id: "metrics-tick")[
                        $"tick {s.Tick.ToString(Inv)}"]
                ],
                Div(Class: "row text-center g-3")[
                    Stat("CPU", $"{s.CpuPercent.ToString("0.0", Inv)}%", "metrics-cpu"),
                    Stat("Active jobs", s.ActiveJobs.ToString(Inv), "metrics-jobs")
                ],
                P(Class: "text-secondary small mb-0 mt-3")[
                    "updated ", Code()[s.At.ToString("HH:mm:ss", Inv)], " · pushed by the background feed"
                ]
            ]
        ];
    }

    private static Component Stat(string label, string value, string id) =>
        Div(Class: "col-6")[
            Div(Class: "fs-3 fw-bold", Id: id)[value],
            Div(Class: "text-secondary small")[label]
        ];
}

// SVG sparkline of the CPU history — a second, independent subscriber. Reuses the
// stateless Sparkline demo (zero JS, server-rendered SVG); the feed's rolling buffer is
// the data series. ValueFormat renders the axis labels as percentages instead of money.
public sealed class MetricsChart(IMetricsFeed feed) : Component
{
    protected override void OnMount() => feed.Updated += StateHasChanged;

    protected override void OnUnmount() => feed.Updated -= StateHasChanged;

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0 h-100")[
            Div(Class: "card-body")[
                H3(Class: "h6 text-secondary text-uppercase small mb-3")["CPU %, last minute"],
                Div(Class: "metrics-chart-container", Id: "metrics-chart",
                    Style: "position: relative; height: 160px;")[
                    Sparkline(
                        feed.State.Recent.Select(p => p.CpuPercent).ToList(),
                        ValueFormat: "0.0'%'",
                        Class: "metrics-chart-svg")
                ]
            ]
        ];
}
