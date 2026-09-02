using System.Globalization;

namespace Rask.Example.Shared.Features;

// One of two independent subscribers to the same IMetricsFeed singleton, to make the
// "one background producer, many decoupled consumers" story concrete: the component
// subscribes to feed.Updated on mount, unsubscribes on unmount, and repaints itself
// straight from the latest snapshot. It knows nothing about MetricsChart — both are
// driven by the background loop in MetricsFeed.
//
// The subscribe/unsubscribe pair mirrors ShowcaseLayout's route.Changed handling
// (ShowcaseLayout.cs:57-59): += in OnMount, -= in OnUnmount so a navigated-away
// component stops repainting and can be collected (no event-handler leak). Updated
// fires from a background thread; StateHasChanged() is thread-safe and a no-op once
// unmounted, so no extra marshalling is needed here (same as the disposable timer probe).

// Numeric readout of the latest tick. Ids are stable so the E2E journey can assert the
// tick count advances without any user interaction.
public sealed partial class MetricsGauge(IMetricsFeed feed) : Component
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    protected override void OnMount() => feed.Updated += StateHasChanged;

    protected override void OnUnmount() => feed.Updated -= StateHasChanged;

    protected override Component? Render()
    {
        var s = feed.State.Current;
        return Div.Class($"{Tw.Card} shadow-sm border-0 h-full")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex justify-between items-baseline flex-wrap items-center mb-3")[
                    H3.Class("text-base font-semibold text-slate-500 dark:text-slate-400 uppercase text-sm mb-0")["System metrics"],
                    Span.Class(Tw.BadgeSecondary).Id("metrics-tick")[
                        $"tick {s.Tick.ToString(Inv)}"]
                ],
                Div.Class("grid grid-cols-12 gap-4 text-center")[
                    Stat("CPU", $"{s.CpuPercent.ToString("0.0", Inv)}%", "metrics-cpu"),
                    Stat("Active jobs", s.ActiveJobs.ToString(Inv), "metrics-jobs")
                ],
                P.Class("text-slate-500 dark:text-slate-400 text-sm mb-0 mt-3")[
                    "updated ", Code[s.At.ToString("HH:mm:ss", Inv)], " · pushed by the background feed"
                ]
            ]
        ];
    }

    private static Component Stat(string label, string value, string id) =>
        Div.Class("col-span-6")[
            Div.Class("text-2xl font-bold").Id(id)[value],
            Div.Class("text-slate-500 dark:text-slate-400 text-sm")[label]
        ];
}
