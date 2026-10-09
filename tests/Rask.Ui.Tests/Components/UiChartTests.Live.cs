using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

public partial class UiChartTests
{
    private const string Measured = "[data-rask-measure] input";

    [Fact]
    public void A_chart_mounted_in_a_live_page_draws_its_rows()
    {
        var page = Page.Render(() => Ui.Chart.Value(Visits)[
            Ui.ChartSvg.Gutter("0").Width(200).Height(100)[Ui.ChartLine.Field((Visit v) => v.Visitors).Curve(Ui.ChartCurve.None), Ui.ChartCursor]
        ]);

        var html = page.Html;

        Assert.Matches("stroke-linejoin=\"round\" d=\"M 0 [0-9.]+ C ", html);
        Assert.Contains("data-ui-chart-hover data-rask-measure", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chart_is_drawn_again_in_the_units_of_the_box_the_browser_measured()
    {
        var page = Page.Render(() => Ui.Chart.Value([0d, 10, 20])[Ui.ChartSvg.Gutter("0")[Ui.ChartLine.Curve(Ui.ChartCurve.None)]]);
        var unmeasured = page.Html;

        var measured = await page.On(Measured).Input("292 97.33");

        // Three rows across the box, the highest at its top: the end of the line is the box's own corner.
        Assert.Contains("viewBox=\"0 0 600 200\"", unmeasured, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 292 97.33\"", measured, StringComparison.Ordinal);
        Assert.Contains(", 292 0\"></path>", measured, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" value=\"292 97.33\"", measured, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0 0")]
    [InlineData("-5 100")]
    [InlineData("NaN 100")]
    [InlineData("Infinity 100")]
    [InlineData("100000 100")]
    [InlineData("300")]
    [InlineData("wide tall")]
    public async Task A_size_that_is_no_box_leaves_the_chart_as_it_was_drawn(string size)
    {
        var page = Page.Render(() => Ui.Chart.Value([0d, 10, 20])[Ui.ChartSvg.Width(300).Height(100)[Ui.ChartLine]]);

        var html = await page.On(Measured).Input(size);

        Assert.Contains("viewBox=\"0 0 300 100\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_that_change_are_drawn_and_read_again_by_a_chart_that_kept_its_last_drawing()
    {
        var rows = new[] { 0d, 10, 20 };
        var page = Page.Render(() => Markup.Div[
            Markup.Button.OnClick(() => rows = [20d, 10, 0, 5])["turn"],
            Ui.Chart.Value(rows)[
                Ui.ChartSvg.Gutter("0").Width(200).Height(100)[Ui.ChartLine.Curve(Ui.ChartCurve.None)],
                Ui.ChartTooltip[Ui.ChartTooltipValue]]]);
        var before = page.Html;

        await page.On(Measured).Input("200 100");
        var same = page.Html;
        var after = await page.On("button").Click();

        Assert.Equal(Line(before), Line(same));
        Assert.StartsWith("M 0 100 C", Line(before), StringComparison.Ordinal);
        Assert.StartsWith("M 0 0 C", Line(after), StringComparison.Ordinal);
        Assert.Contains("data-rask-plot-area=\"0 0.33333 0.66667 1\"", after, StringComparison.Ordinal);
        Assert.Contains("data-rask-plot-text=\"20&#xA;10&#xA;0&#xA;5\"", after, StringComparison.Ordinal);
    }

    private static string Line(string html) => Regex.Match(html, "stroke-linejoin=\"round\" d=\"([^\"]*)\"").Groups[1].Value;
}
