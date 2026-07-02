namespace Rask.Example.Shared.Features;

// The two background-service widgets promoted out of the former BackgroundServicePage so the Lifecycle
// guide can host them live. Both subscribe independently to the app-wide IMetricsFeed singleton (whose
// loop ticks whether or not this demo is mounted) and repaint themselves on each tick.
public sealed class BackgroundMetricsDemo : Component
{
    protected override RenderResult Render() =>
        Div(Class: "row g-4")[
            Div(Class: "col-lg-5")[
                MetricsGauge()
            ],
            Div(Class: "col-lg-7")[
                MetricsChart()
            ]
        ];
}
