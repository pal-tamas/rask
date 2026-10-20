namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartBarExample : Component
{
    private static readonly Visit[] Visits =
    [
        .. new[] { 171, 227, 269, 223, 249, 251, 300, 246, 162, 115, 176, 144, 184, 269, 259, 267 }
            .Select((visitors, day) => new Visit(new DateOnly(2026, 1, 1).AddDays(day), visitors)),
    ];

    protected override Component? Render() =>
        Ui.Chart.Value(Visits).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartBar.Field((Visit v) => v.Visitors).Class("text-blue-500").Radius("0").Width("85%"),
                Ui.ChartAxis.X.Field((Visit v) => v.Day).TickCount(8)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.Y.Format(new() { UseGrouping = true })[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor.Type(Ui.ChartCursorType.Area)
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Visit v) => v.Day),
                Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")
            ]
        ];

    private sealed record Visit(DateOnly Day, int Visitors);
}
