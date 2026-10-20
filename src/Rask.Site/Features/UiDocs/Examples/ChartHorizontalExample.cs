namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartHorizontalExample : Component
{
    private static readonly Sale[] Sales =
    [
        new("Beauty", 342), new("Books", 356), new("Clothes", 432), new("Garden", 398), new("Pets", 487), new("Sports", 548), new("Toys", 643),
    ];

    protected override Component? Render() =>
        Ui.Chart.Horizontal().Value(Sales).Class("aspect-2/1")[
            Ui.ChartSvg[
                Ui.ChartBar.Field((Sale s) => s.Online).Class("text-blue-500").Radius("4 0").Width("70%"),
                Ui.ChartAxis.Y.Field((Sale s) => s.Category)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                Ui.ChartAxis.X[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor.Type(Ui.ChartCursorType.Area)
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Sale s) => s.Category),
                Ui.ChartTooltipValue.Field((Sale s) => s.Online).Label("Orders")
            ]
        ];

    private sealed record Sale(string Category, int Online);
}
