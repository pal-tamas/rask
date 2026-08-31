namespace Rask.Example.Shared.Features;

// The two background-service widgets promoted out of the former BackgroundServicePage so the Lifecycle
// guide can host them live. Both subscribe independently to the app-wide IMetricsFeed singleton (whose
// loop ticks whether or not this demo is mounted) and repaint themselves on each tick.
public sealed partial class BackgroundMetricsDemo : Component
{
    protected override Component? Render() =>
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("lg:col-span-5")[
                MetricsGauge
            ],
            Div.Class("lg:col-span-7")[
                MetricsChart
            ]
        ];
}
