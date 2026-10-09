using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

public partial class UiChartTests
{
    private const string Measured = MeasuredCharts.Field;

    [Fact]
    public void A_chart_mounted_in_a_live_page_is_an_empty_box_that_asks_to_be_measured()
    {
        var page = Page.Render(() => Ui.Chart.Value(Visits)[
            Ui.ChartSvg.Gutter("0")[Ui.ChartLine.Field((Visit v) => v.Visitors).Curve(Ui.ChartCurve.None), Ui.ChartCursor]
        ]);

        var html = page.Html;

        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-chart-hover data-rask-measure", html, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" value=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chart_is_drawn_in_the_units_of_the_box_the_browser_measured_and_again_when_it_changes()
    {
        var page = Page.Render(() => Ui.Chart.Value([0d, 10, 20])[Ui.ChartSvg.Gutter("0")[Ui.ChartLine.Curve(Ui.ChartCurve.None)]]);

        var measured = await page.On(Measured).Input("292 97.33");
        var narrowed = await page.On(Measured).Input("146 48.67");

        // Three rows across the box, the highest at its top: the end of the line is the box's own corner.
        Assert.Contains("viewBox=\"0 0 292 97.33\"", measured, StringComparison.Ordinal);
        Assert.Contains(", 292 0\"></path>", measured, StringComparison.Ordinal);
        Assert.Contains("<input type=\"hidden\" value=\"292 97.33\"", measured, StringComparison.Ordinal);
        Assert.Contains("viewBox=\"0 0 146 48.67\"", narrowed, StringComparison.Ordinal);
        Assert.Contains(", 146 0\"></path>", narrowed, StringComparison.Ordinal);
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
        var page = MeasuredCharts.Page(Ui.Chart.Value([0d, 10, 20])[Ui.ChartSvg[Ui.ChartLine]], (300, 100));

        var html = await page.On(Measured).Input(size);

        Assert.Contains("viewBox=\"0 0 300 100\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0 0")]
    [InlineData("wide tall")]
    public async Task A_size_that_is_no_box_leaves_a_chart_that_was_never_measured_undrawn(string size)
    {
        var page = Page.Render(() => Ui.Chart.Value([0d, 10, 20])[Ui.ChartSvg[Ui.ChartLine]]);

        var html = await page.On(Measured).Input(size);

        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rows_that_change_are_drawn_and_read_again_by_a_chart_that_kept_its_last_drawing()
    {
        var rows = new[] { 0d, 10, 20 };
        var page = Page.Render(() => Markup.Div[
            Markup.Button.OnClick(() => rows = [20d, 10, 0, 5])["turn"],
            Ui.Chart.Value(rows)[
                Ui.ChartSvg.Gutter("0")[Ui.ChartLine.Curve(Ui.ChartCurve.None)],
                Ui.ChartTooltip[Ui.ChartTooltipValue]]]);
        var before = await page.On(Measured).Input("200 100");

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
