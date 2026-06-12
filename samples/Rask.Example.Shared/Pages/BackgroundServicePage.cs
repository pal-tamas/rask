using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

// Showcase for an app-wide background process driving the UI. The page itself is static —
// all the live behaviour comes from MetricsGauge and MetricsChart, two independent
// components that subscribe to the IMetricsFeed singleton (MetricsFeed.cs). The feed's
// background loop ticks once a second whether or not this page is mounted, so navigating
// away and back shows a higher tick count — proof the producer is decoupled from the UI.
[Route("background")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BackgroundServicePage : Component
{
    protected override RenderResult Head => Title()["Background service — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Background service",
            "An app-wide background process pushing updates to the UI. A single IMetricsFeed singleton runs its own loop and raises an event each tick; the two widgets below each subscribe independently (feed.Updated += StateHasChanged) and repaint themselves. Unlike the Live ticker — whose poll loop lives inside one component — this producer is decoupled from the component tree: it keeps ticking across navigations (and, on the Server, across every session). Navigate away and back to see the tick count has advanced while nothing was rendering it."),
        Div(Class: "row g-4")[
            Div(Class: "col-lg-5")[
                MetricsGauge()
            ],
            Div(Class: "col-lg-7")[
                MetricsChart()
            ]
        ],
        H2(Class: "h4 mt-5 mb-3")["The pattern"],
        P(Class: "text-secondary")[
            "The producer is a DI ", Code()["AddSingleton<IMetricsFeed, MetricsFeed>()"],
            " — one instance for the whole app. Each consumer is a tiny component that subscribes on mount and ",
            Strong()["unsubscribes on unmount"], " so it stops repainting (and can be collected) once it leaves the tree."
        ],
        CodeSample(
            """
            // The background producer — a singleton, not tied to any component.
            public sealed class MetricsFeed : IMetricsFeed, IAsyncDisposable
            {
                private MetricsSnapshot _state;
                public MetricsSnapshot State => Volatile.Read(ref _state);
                public event Action? Updated;

                public MetricsFeed() => _loop = RunAsync(_cts.Token);

                private async Task RunAsync(CancellationToken ct)
                {
                    while (!ct.IsCancellationRequested)
                    {
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        // Publish one immutable snapshot by reference, THEN notify.
                        Volatile.Write(ref _state, Step(_state, DateTimeOffset.UtcNow));
                        Updated?.Invoke();
                    }
                }

                public async ValueTask DisposeAsync() { await _cts.CancelAsync(); /* … */ }
            }

            // A consumer — subscribes on mount, unsubscribes on unmount.
            public sealed class MetricsGauge(IMetricsFeed feed) : Component
            {
                protected override void OnMount()   => feed.Updated += StateHasChanged;
                protected override void OnUnmount() => feed.Updated -= StateHasChanged;
                protected override RenderResult Render() =>
                    Span()[$"CPU {feed.State.Current.CpuPercent}%"];
            }
            """,
            Notes:
            "The loop runs on a background thread, so StateHasChanged() crosses threads here — that's safe: it schedules a render under the subscriber's own session lock and is a no-op once the component unmounts (so a tick racing an unsubscribe is harmless). State is an immutable snapshot swapped by reference, so a render walking it on the UI thread never sees a half-built value. The feed is registered as a singleton (app-wide, shared) — deliberately unlike the scoped DemoUserProvider, because a metric stream is public but a user's principal must never be shared across sessions."),
        Div(Class: "alert alert-info d-flex align-items-start mt-3")[
            I(Class: "bi bi-info-circle-fill me-3 fs-4"),
            Div()[
                Strong()["Decoupled lifetime."],
                " The feed is created on first resolution and then runs for the app's lifetime, disposed by the host on shutdown (",
                Code()["IAsyncDisposable"],
                "). It advances independently of any page — swap the synthetic ",
                Code()["Step"], " for a real metrics endpoint or message bus and nothing else changes."
            ]
        ]
    ];
}
