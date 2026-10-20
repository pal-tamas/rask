namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartExample : Component
{
    // The numbers on Flux's own page.
    private static readonly Visit[] Visits =
    [
        .. new[] { 171, 227, 269, 223, 249, 251, 300, 246, 162, 115, 176, 144, 184, 269, 259, 267 }
            .Select((visitors, day) => new Visit(new DateOnly(2026, 1, 1).AddDays(day), visitors)),
    ];

    protected override Component? Render() =>
        Ui.Chart.Value(Visits).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Visit v) => v.Visitors).Class("text-pink-500 dark:text-pink-400"),
                Ui.ChartAxis.X.Field((Visit v) => v.Day)[Ui.ChartAxisLine, Ui.ChartAxisTick],
                Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Visit v) => v.Day),
                Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")
            ]
        ];

    private sealed record Visit(DateOnly Day, int Visitors);
}
