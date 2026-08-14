namespace Rask.Example.Shared.Features;

// The two background-service widgets promoted out of the former BackgroundServicePage so the Lifecycle
// guide can host them live. Both subscribe independently to the app-wide IMetricsFeed singleton (whose
// loop ticks whether or not this demo is mounted) and repaint themselves on each tick.
public sealed partial class BackgroundMetricsDemo : Component
{
    protected override Component? Render() =>
        BsRow.Gutter(4)[
            BsCol.Lg(5)[
                MetricsGauge
            ],
            BsCol.Lg(7)[
                MetricsChart
            ]
        ];
}
