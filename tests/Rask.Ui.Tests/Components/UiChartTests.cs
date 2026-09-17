using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The server-drawn chart: <c>UiChart.Data(rows).Label(...)[c =&gt; [c.X(...), c.Line(...)]]</c>.
/// </summary>
/// <remarks>
///     A chart is a picture of numbers, so most of what is worth pinning is that the numbers survive: the scale is
///     round and includes zero, the shapes land where the values say, and every value is in the table a screen
///     reader reads instead of the drawing.
/// </remarks>
public partial class UiChartTests : global::Rask.Core.RaskMarkup
{
    private sealed record Sale(string Month, decimal Revenue, int Orders, double Cost);

    private static readonly Sale[] Sales =
    [
        new("Jan", 120m, 3, 80),
        new("Feb", 480m, 9, 150.5),
        new("Mar", 310m, 6, 90),
    ];

    private static string Chart(params Func<UiChart<Sale>, global::Rask.Core.Component?>[] series) =>
        // Keys stripped: a keyed cell carries data-rask-key ahead of its own attributes, which is identity for the diff
        // and noise for these assertions.
        Regex.Replace(
            UiChart.Data(Sales).Label("Revenue")[c => [c.X(s => s.Month), .. series.Select(f => f(c))]].ToHtml(),
            " data-rask-key=\"[^\"]*\"",
            "");

    [Fact]
    public void It_is_a_named_figure_with_the_drawing_hidden_from_assistive_technology()
    {
        var html = Chart(c => c.Line(s => s.Revenue));

        Assert.Matches("<figure[^>]*aria-label=\"Revenue\"", html);
        Assert.Matches("<svg[^>]*aria-hidden=\"true\"", html);
    }

    [Fact]
    public void Every_value_is_in_a_table_a_screen_reader_reads_instead()
    {
        var html = Chart(c => c.Line(s => s.Revenue).Label("Revenue"), c => c.Bar(s => s.Orders).Label("Orders"));

        Assert.Contains("<table class=\"sr-only\">", html, StringComparison.Ordinal);
        Assert.Contains("<caption>Revenue</caption>", html, StringComparison.Ordinal);
        Assert.Matches("<th scope=\"col\">Orders</th>", html);
        Assert.Matches("<th scope=\"row\">Feb</th><td>480</td><td>9</td>", html);
    }

    [Fact]
    public void Every_numeric_type_a_model_holds_reads_without_a_cast()
    {
        // decimal, int and double: the three a sales row actually carries. Compiling is most of this test.
        var html = Chart(c => c.Line(s => s.Revenue), c => c.Bar(s => s.Orders), c => c.Area(s => s.Cost));

        Assert.Equal(3, Regex.Matches(html, "<th scope=\"col\">").Count);
    }

    [Fact]
    public void The_value_axis_is_round_and_starts_at_zero()
    {
        var scale = UiChartScale.For([120, 480, 310], null, null);

        Assert.Equal(0, scale.Min);
        Assert.Equal(500, scale.Max);
        Assert.Equal([0, 100, 200, 300, 400, 500], scale.Ticks);
    }

    [Fact]
    public void A_negative_value_takes_the_axis_below_zero()
    {
        var scale = UiChartScale.For([-30, 80], null, null);

        Assert.True(scale.Min < 0);
        Assert.Contains(0d, scale.Ticks);
    }

    [Fact]
    public void Nothing_to_draw_is_still_a_scale()
    {
        // No rows, or every value zero: the axis still has two ends a division can use.
        var scale = UiChartScale.For([], null, null);

        Assert.True(scale.Max > scale.Min);
    }

    [Fact]
    public void A_line_passes_through_the_middle_of_each_rows_band()
    {
        // Three rows across 1000 units: bands of 333.33, middles at 166.67, 500 and 833.33. The axis tops out at
        // 500, so 120 sits at 300 - 120/500*300 = 228.
        var html = Chart(c => c.Line(s => s.Revenue));

        Assert.Contains("d=\"M166.67 228L500 12L833.33 114\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_area_closes_its_shape_along_the_zero_line()
    {
        var html = Chart(c => c.Area(s => s.Revenue));

        Assert.Contains("L833.33 300L166.67 300Z", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Bars_side_by_side_share_their_band()
    {
        var html = Chart(c => c.Bar(s => s.Orders), c => c.Bar(s => s.Orders));

        Assert.Equal(6, Regex.Matches(html, "<rect ").Count);
    }

    [Fact]
    public void A_series_without_a_tone_takes_the_next_colour_in_turn()
    {
        var html = Chart(c => c.Line(s => s.Revenue), c => c.Line(s => s.Cost), c => c.Line(s => s.Orders).Tone(UiTone.Error));

        Assert.Contains("stroke-primary", html, StringComparison.Ordinal);
        Assert.Contains("stroke-secondary", html, StringComparison.Ordinal);
        Assert.Contains("stroke-error", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_legend_appears_once_there_is_more_than_one_series()
    {
        Assert.DoesNotContain("flex flex-wrap gap-x-4", Chart(c => c.Line(s => s.Revenue)), StringComparison.Ordinal);
        Assert.Contains("flex flex-wrap gap-x-4",
            Chart(c => c.Line(s => s.Revenue), c => c.Line(s => s.Cost)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_arm_of_a_conditional_is_no_series()
    {
        var showCost = false;
        var html = Chart(c => c.Line(s => s.Revenue), c => showCost ? c.Area(s => s.Cost) : null);

        Assert.Single(Regex.Matches(html, "<th scope=\"col\">"));
    }

    [Fact]
    public void Values_are_written_in_the_format_the_chart_is_given()
    {
        var html = UiChart.Data(Sales).Label("Revenue").Format("C0")[c => [c.X(s => s.Month), c.Line(s => s.Revenue)]]
            .ToHtml();

        Assert.Contains(480.ToString("C0", CultureInfo.CurrentCulture), System.Net.WebUtility.HtmlDecode(html),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Its_colours_are_in_the_shipped_sheet()
    {
        // Written only by the chart, so a class Tailwind failed to see would draw an invisible line.
        foreach (var selector in new[] { ".stroke-primary", ".fill-primary", ".fill-primary\\/15", ".bg-primary" })
        {
            Assert.Contains(selector, UiStylesheet.Css, StringComparison.Ordinal);
        }
    }
}
