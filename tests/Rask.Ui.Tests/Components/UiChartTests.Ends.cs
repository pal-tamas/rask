using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Dates crowded down to their two ends (#1301): whether the first and the last still hold the plot's edges in.
/// </summary>
/// <remarks>
///     Every case is a dataset handed to Flux's live <c>&lt;ui-chart&gt;</c> on fluxui.dev at its phone box (292 ×
///     97.33), with Flux's plot edges read back from its grid line. The rule they pin: the dates that hold the edges in
///     are the ones still showing when the remedies are tried on the whole box, before the plot is pulled in.
/// </remarks>
public partial class UiChartTests
{
    private sealed record Stock(DateTime Date, double Price);

    private const double PhoneWidth = 292;
    private const double PhoneHeight = 97.328125;

    // The area chart's example: 48 rows ten minutes apart, the value axis on the right.
    [Theory]
    [InlineData("10:31:30", 25.6953125)]
    [InlineData("10:32:30", 8)]
    [InlineData("10:00:30", 25.6953125)]
    [InlineData("06:38:27", 23.109375)]
    [InlineData("10:43:15", 8)]
    [InlineData("21:37:20", 22.7109375)]
    [InlineData("02:53:38", 8)]
    public void A_first_date_holds_the_left_edge_in_only_when_the_whole_box_fits_it_beside_the_third(string start, double left)
    {
        var first = DateTime.Parse("2026-11-16T" + start, CultureInfo.InvariantCulture);
        var rows = Enumerable.Range(0, 48).Select(i => new Stock(first.AddMinutes(10 * i), 2000)).ToList();

        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(rows)[
                Ui.ChartSvg[
                    Ui.ChartLine.Field((Stock s) => s.Price).Curve(Ui.ChartCurve.None),
                    Ui.ChartArea.Field((Stock s) => s.Price).Curve(Ui.ChartCurve.None),
                    Ui.ChartAxis.Y.Position(Ui.Position.Right).TickPrefix("$")
                        .Format(new() { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 })[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                    Ui.ChartAxis.X.Field((Stock s) => s.Date)[Ui.ChartAxisTick, Ui.ChartAxisLine]
                ]
            ],
            (PhoneWidth, PhoneHeight)));

        var (plotLeft, plotRight) = PlotEdges(html);
        Assert.Equal(2, Regex.Matches(html, "style=\"display: inline;\"").Count);
        Assert.Equal(left, plotLeft, 0.01);
        Assert.Equal(240.9375, plotRight, 0.01);
    }

    // The first example's shape: nine daily rows, the value axis on the left.
    [Theory]
    [InlineData("04:37:45", 270.3995666503906)]
    [InlineData("20:54:40", 284)]
    public void A_last_date_holds_the_right_edge_in_only_when_the_whole_box_fits_it_beside_the_one_two_before(string start, double right)
    {
        var first = DateTime.Parse("2026-11-16T" + start, CultureInfo.InvariantCulture);
        var rows = Enumerable.Range(0, 9).Select(i => new Stock(first.AddDays(i), 250)).ToList();

        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(rows)[
                Ui.ChartSvg[
                    Ui.ChartLine.Field((Stock s) => s.Price),
                    Ui.ChartAxis.X.Field((Stock s) => s.Date)[Ui.ChartAxisLine, Ui.ChartAxisTick],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick]
                ]
            ],
            (PhoneWidth, PhoneHeight)));

        var (plotLeft, plotRight) = PlotEdges(html);
        Assert.Equal(2, Regex.Matches(html, "style=\"display: inline;\"").Count);
        Assert.Equal(42.46875, plotLeft, 0.01);
        Assert.Equal(right, plotRight, 0.01);
    }

    private static (double Left, double Right) PlotEdges(string html)
    {
        var line = GridLine().Match(html);
        return (double.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(line.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex("x1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\"")]
    private static partial Regex GridLine();
}
