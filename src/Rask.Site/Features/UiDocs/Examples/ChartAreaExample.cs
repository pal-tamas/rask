namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartAreaExample : Component
{
    // A price that climbs faster every minute: worked out, so the drawing is the same on every visit.
    private static readonly Price[] Prices =
    [
        .. Enumerable.Range(0, 21)
            .Select(minute => new Price(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified).AddMinutes(minute), 200 + (minute * minute * 7))),
    ];

    protected override Component? Render() =>
        Ui.Chart.Value(Prices).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Price p) => p.Stock).Class("text-blue-500 dark:text-blue-400").Curve(Ui.ChartCurve.None),
                Ui.ChartArea.Field((Price p) => p.Stock).Class("text-blue-200/50 dark:text-blue-400/30").Curve(Ui.ChartCurve.None),
                Ui.ChartAxis.Y.Position(Ui.Position.Right).TickPrefix("$")
                    .Format(new() { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 })[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartAxis.X.Field((Price p) => p.At)[Ui.ChartAxisTick, Ui.ChartAxisLine]
            ]
        ];

    private sealed record Price(DateTime At, double Stock);
}
