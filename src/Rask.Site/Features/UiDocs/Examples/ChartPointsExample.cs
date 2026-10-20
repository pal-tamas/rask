namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartPointsExample : Component
{
    private static readonly Reading[] Readings =
    [
        .. new[] { 268, 243, 236, 201, 228, 251, 231, 267, 202, 206, 280, 268, 265, 215, 254, 237, 253, 266, 257, 297, 269 }
            .Select((memory, minute) => new Reading(new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Unspecified).AddMinutes(minute), memory)),
    ];

    protected override Component? Render() =>
        Ui.Chart.Value(Readings).Class("aspect-3/1")[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Reading r) => r.Memory).Class("text-pink-500"),
                Ui.ChartPoint.Field((Reading r) => r.Memory).Class("text-pink-400"),
                Ui.ChartAxis.X.Field((Reading r) => r.At)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.Y.Format(new() { Style = Ui.ChartFormatStyle.Unit, Unit = "megabyte" })[Ui.ChartAxisGrid, Ui.ChartAxisTick]
            ]
        ];

    private sealed record Reading(DateTime At, int Memory);
}
