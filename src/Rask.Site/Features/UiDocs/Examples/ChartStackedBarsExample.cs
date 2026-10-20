namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartStackedBarsExample : Component
{
    private static readonly Sale[] Sales =
    [
        new("Beauty", 342, 215, 108), new("Books", 356, 256, 215), new("Clothes", 432, 284, 176), new("Garden", 398, 190, 143),
        new("Pets", 487, 301, 122), new("Sports", 548, 327, 190), new("Toys", 643, 412, 236),
    ];

    protected override Component? Render() =>
        Ui.Chart.Value(Sales)[
            Ui.ChartViewport.Class("aspect-3/1")[
                Ui.ChartSvg[
                    Ui.ChartStack.Width("65%")[
                        Ui.ChartBar.Field((Sale s) => s.Online).Class("text-blue-600"),
                        Ui.ChartBar.Field((Sale s) => s.Retail).Class("text-blue-400"),
                        Ui.ChartBar.Field((Sale s) => s.Wholesale).Class("text-blue-300").Radius("4 0")
                    ],
                    Ui.ChartAxis.X.Field((Sale s) => s.Category)[Ui.ChartAxisTick, Ui.ChartAxisLine],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            Div.Class("flex justify-center gap-4 pt-4")[
                Ui.ChartLegend.Key("online").Label("Online")[Ui.ChartLegendIndicator.Class("bg-blue-600")],
                Ui.ChartLegend.Key("retail").Label("Retail")[Ui.ChartLegendIndicator.Class("bg-blue-400")],
                Ui.ChartLegend.Key("wholesale").Label("Wholesale")[Ui.ChartLegendIndicator.Class("bg-blue-300")]
            ]
        ];

    private sealed record Sale(string Category, int Online, int Retail, int Wholesale);
}
