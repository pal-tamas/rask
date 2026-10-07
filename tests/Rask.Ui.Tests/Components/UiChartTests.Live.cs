using Rask.Testing;

namespace Rask.UiTests.Components;

public partial class UiChartTests
{
    [Fact]
    public void A_chart_mounted_in_a_live_page_draws_its_rows()
    {
        var page = Page.Render(() => Ui.Chart.Value(Visits)[
            Ui.ChartSvg.Gutter("0").Width(200).Height(100)[Ui.ChartLine.Field((Visit v) => v.Visitors).Curve(Ui.ChartCurve.None), Ui.ChartCursor]
        ]);

        var html = page.Html;

        Assert.Matches("stroke-linejoin=\"round\" d=\"M 0 [0-9.]+ C ", html);
        Assert.Contains("data-ui-chart-hover", html, StringComparison.Ordinal);
    }
}
