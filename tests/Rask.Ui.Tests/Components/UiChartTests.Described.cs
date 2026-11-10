using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     What a chart says to a screen reader: its drawing is an image named by its kind, its series and its reach, and
///     described by its rows, in a table no one sees.
/// </summary>
public partial class UiChartTests
{
    private sealed record Share(string Name, double Part);

    private sealed record Sale(string Region, double North, double South, double East, double West, double Online, double Partner);

    private static readonly Share[] Shares = [new("Rent", 40), new("Food", 30), new("Travel", 20), new("R&D <lab>", 10)];

    private static readonly Sale[] Sales =
    [
        new("Q1", 10, 20, 30, 40, 50, 60),
        new("Q2", 11, 21, 31, 41, 51, 61),
        new("Q3", 12, 22, 32, 42, 52, 62),
    ];

    [Fact]
    public void A_line_chart_is_an_image_named_by_its_kind_its_series_and_its_reach()
    {
        var html = Intro();

        var svg = Svg(html);

        Assert.Contains("role=\"img\"", svg, StringComparison.Ordinal);
        Assert.Equal("Line chart: Visitors. 16 data points, 9/22/2026 to 10/7/2026. Values from 115 to 300.", Label(svg));
    }

    [Fact]
    public void A_charts_drawing_is_described_by_its_rows_in_a_table_no_one_sees()
    {
        var html = Intro();

        var described = Regex.Match(Svg(html), "aria-describedby=\"([^\"]+)\"").Groups[1].Value;
        var table = Table(html);

        Assert.StartsWith("ui-chart-rows-", described, StringComparison.Ordinal);
        Assert.StartsWith("<table class=\"sr-only\" id=\"" + described + "\">", table, StringComparison.Ordinal);
        Assert.Equal(["Date", "Visitors"], Headings(table));
        Assert.Equal(16, Rows(table).Length);
        Assert.Equal(("9/22/2026", "171"), Rows(table)[0]);
        Assert.Equal(("10/7/2026", "267"), Rows(table)[^1]);
    }

    [Fact]
    public void Each_chart_on_a_page_is_described_by_its_own_table()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Div[
                Ui.Chart.Value([1, 2])[Ui.ChartSvg[Ui.ChartLine]],
                Ui.Chart.Value([3, 4])[Ui.ChartSvg[Ui.ChartLine]]
            ],
            (200, 100),
            (200, 100)));

        var ids = Regex.Matches(html, "aria-describedby=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();
        var tables = Regex.Matches(html, "<table class=\"sr-only\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray();

        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids, tables);
    }

    [Fact]
    public async Task A_drawing_keeps_the_id_it_is_described_by_when_it_is_drawn_again()
    {
        var page = MeasuredCharts.Page(Ui.Chart.Value([1, 2, 3])[Ui.ChartSvg[Ui.ChartLine]], (200, 100));
        var first = Regex.Match(page.Html, "aria-describedby=\"([^\"]+)\"").Groups[1].Value;

        var field = page.FindAll(MeasuredCharts.Field).Single().Attribute("data-rask-on-input")!;
        await page.Invoke(field, """{"value":"300 120"}""");

        Assert.Contains("viewBox=\"0 0 300 120\"", page.Html, StringComparison.Ordinal);
        Assert.Equal(first, Regex.Match(page.Html, "aria-describedby=\"([^\"]+)\"").Groups[1].Value);
        Assert.Contains("<table class=\"sr-only\" id=\"" + first + "\">", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chart_not_yet_measured_draws_nothing_but_its_rows_are_there_to_be_read()
    {
        var html = InEnglish(() => Ui.Chart.Value([5, 7])[Ui.ChartSvg[Ui.ChartLine]].ToHtml());

        var table = Table(html);

        Assert.DoesNotContain("<svg", html, StringComparison.Ordinal);
        Assert.Equal(["Row", "Value"], Headings(table));
        Assert.Equal([("1", "5"), ("2", "7")], Rows(table));
    }

    [Fact]
    public void An_area_under_its_line_is_one_series_and_the_chart_is_an_area_chart()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Visits)[
                Ui.ChartSvg[
                    Ui.ChartArea.Field((Visit v) => v.Visitors),
                    Ui.ChartLine.Field((Visit v) => v.Visitors),
                    Ui.ChartPoint.Field((Visit v) => v.Visitors),
                    Ui.ChartAxis.X.Field((Visit v) => v.Day)
                ],
                Ui.ChartTooltip[Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")]
            ],
            (606, 202)));

        var table = Table(html);

        Assert.StartsWith("Area chart: Visitors. 16 data points", Label(Svg(html)), StringComparison.Ordinal);
        Assert.Equal(["Date", "Visitors"], Headings(table));
    }

    [Fact]
    public void Series_are_named_by_the_tooltip_values_that_read_the_same_numbers_in_whatever_order_they_are_declared()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Sales)[
                Ui.ChartSvg[
                    Ui.ChartLine.Field((Sale s) => s.North),
                    Ui.ChartLine.Field((Sale s) => s.South),
                    Ui.ChartLine.Field((Sale s) => s.East),
                    Ui.ChartLine.Field((Sale s) => s.West),
                    Ui.ChartLine.Field((Sale s) => s.Online),
                    Ui.ChartLine.Field((Sale s) => s.Partner),
                    Ui.ChartAxis.X.Field((Sale s) => s.Region)
                ],
                Ui.ChartTooltip[
                    Ui.ChartTooltipHeading.Field((Sale s) => s.Region),
                    Ui.ChartTooltipValue.Field((Sale s) => s.Partner).Label("Partner"),
                    Ui.ChartTooltipValue.Field((Sale s) => s.Online).Label("Online"),
                    Ui.ChartTooltipValue.Field((Sale s) => s.West).Label("West"),
                    Ui.ChartTooltipValue.Field((Sale s) => s.East).Label("East"),
                    Ui.ChartTooltipValue.Field((Sale s) => s.South).Label("South"),
                    Ui.ChartTooltipValue.Field((Sale s) => s.North).Label("North")
                ]
            ],
            (606, 202)));

        var table = Table(html);

        Assert.Equal("Line chart: North, South, East, West, Online, Partner. 3 data points, Q1 to Q3. Values from 10 to 62.", Label(Svg(html)));
        Assert.Equal(["Label", "North", "South", "East", "West", "Online", "Partner"], Headings(table));
        Assert.Matches("<tr><th scope=\"row\">Q2</th><td>11</td><td>21</td><td>31</td><td>41</td><td>51</td><td>61</td></tr>", table);
    }

    [Fact]
    public void A_series_named_by_a_legend_is_written_as_its_legend_writes_it()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Visits)[
                Ui.ChartViewport[Ui.ChartSvg[Ui.ChartLine.Field((Visit v) => v.Revenue), Ui.ChartAxis.X.Field((Visit v) => v.Day)]],
                Div[Ui.ChartLegend.Field((Visit v) => v.Revenue).Label("Revenue").Format(new UiChartFormat { Style = Ui.ChartFormatStyle.Currency, Currency = "USD" })]
            ],
            (606, 202)));

        var table = Table(html);

        Assert.Equal(["Date", "Revenue"], Headings(table));
        Assert.Equal(("9/22/2026", "$1,795.50"), Rows(table)[0]);
        Assert.EndsWith("Values from $1,207.50 to $3,150.00.", Label(Svg(html)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tooltip_values_prefix_suffix_and_format_write_the_tables_cells()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Visits)[
                Ui.ChartSvg[Ui.ChartLine.Field((Visit v) => v.Visitors), Ui.ChartAxis.X.Field((Visit v) => v.Day)],
                Ui.ChartTooltip[
                    Ui.ChartTooltipHeading.Field((Visit v) => v.Day).Format(new UiChartFormat { DateStyle = Ui.ChartFormatLength.Medium }),
                    Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors").Prefix("~").Suffix(" people")
                ]
            ],
            (606, 202)));

        var table = Table(html);

        Assert.Equal(("Sep 22, 2026", "~171 people"), Rows(table)[0]);
        Assert.Equal("Line chart: Visitors. 16 data points, Sep 22, 2026 to Oct 7, 2026. Values from ~115 people to ~300 people.", Label(Svg(html)));
    }

    [Fact]
    public void Bars_nothing_names_are_numbered_series_and_the_chart_is_a_bar_chart()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Sales)[
                Ui.ChartSvg[
                    Ui.ChartStack[Ui.ChartBar.Field((Sale s) => s.North), Ui.ChartBar.Field((Sale s) => s.South)],
                    Ui.ChartAxis.X.Field((Sale s) => s.Region)
                ]
            ],
            (606, 202)));

        var table = Table(html);

        Assert.Equal("Bar chart: Series 1, Series 2. 3 data points, Q1 to Q3. Values from 10 to 22.", Label(Svg(html)));
        Assert.Equal(["Label", "Series 1", "Series 2"], Headings(table));
    }

    [Fact]
    public void Grouped_bars_lying_on_their_side_are_named_along_the_Y_axis()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Sales).Horizontal()[
                Ui.ChartSvg[
                    Ui.ChartGroup[Ui.ChartBar.Field((Sale s) => s.East), Ui.ChartBar.Field((Sale s) => s.West)],
                    Ui.ChartAxis.Y.Field((Sale s) => s.Region)
                ],
                Ui.ChartLegend.Field((Sale s) => s.West).Label("West"),
                Ui.ChartLegend.Field((Sale s) => s.East).Label("East")
            ],
            (606, 202)));

        var table = Table(html);

        Assert.Equal("Bar chart: East, West. 3 data points, Q1 to Q3. Values from 30 to 42.", Label(Svg(html)));
        Assert.Equal(("Q1", "30"), Rows(table)[0]);
    }

    [Fact]
    public void A_pie_is_named_by_its_slices_and_its_rows_are_the_slices()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Shares)[
                Ui.ChartSvg[Ui.ChartPie.Field((Share s) => s.Part).LabelField((Share s) => s.Name)],
                Ui.ChartTooltip[Ui.ChartTooltipValue.Field((Share s) => s.Part).Label("Share").Suffix("%")]
            ],
            (220, 220)));

        var table = Table(html);

        Assert.Equal("Pie chart: Share. 4 slices, Rent to R&amp;D &lt;lab&gt;. Values from 10% to 40%.", Regex.Match(Svg(html), "aria-label=\"([^\"]*)\"").Groups[1].Value);
        Assert.Equal(["Label", "Share"], Headings(table));
        Assert.Contains("<tr><th scope=\"row\">R&amp;D &lt;lab&gt;</th><td>10%</td></tr>", table, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pie_with_a_hole_is_a_donut_chart()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Shares)[Ui.ChartSvg[Ui.ChartPie.Field((Share s) => s.Part).LabelField((Share s) => s.Name).InnerRadius("60%")]],
            (220, 220)));

        var label = Label(Svg(html));

        Assert.StartsWith("Donut chart: Value. 4 slices, Rent to R&D <lab>.", label, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chart_with_no_rows_says_so_and_its_table_has_headings_alone()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Array.Empty<Visit>())[
                Ui.ChartSvg[Ui.ChartLine.Field((Visit v) => v.Visitors), Ui.ChartLine.Field((Visit v) => v.Revenue)],
                Ui.ChartTooltip[
                    Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors"),
                    Ui.ChartTooltipValue.Field((Visit v) => v.Revenue).Label("Revenue")
                ]
            ],
            (606, 202)));

        var table = Table(html);

        Assert.Equal("Line chart: Visitors, Revenue. No data.", Label(Svg(html)));
        Assert.Equal(["Row", "Visitors", "Revenue"], Headings(table));
        Assert.Empty(Rows(table));
    }

    [Fact]
    public void A_chart_of_one_row_names_it_alone_and_one_value_alone()
    {
        var html = InEnglish(() => MeasuredCharts.Html(Ui.Chart.Value([42])[Ui.ChartSvg[Ui.ChartLine]], (200, 100)));

        var label = Label(Svg(html));

        Assert.Equal("Line chart: Value. 1 data point, 1. Values from 42 to 42.", label);
    }

    [Fact]
    public void A_chart_of_points_alone_is_a_scatter_chart_and_one_of_nothing_is_a_chart()
    {
        var points = InEnglish(() => MeasuredCharts.Html(Ui.Chart.Value([1, 2])[Ui.ChartSvg[Ui.ChartPoint]], (200, 100)));

        var nothing = InEnglish(() => MeasuredCharts.Html(Ui.Chart.Value([1, 2])[Ui.ChartSvg[Ui.ChartAxis.Y]], (200, 100)));

        Assert.StartsWith("Scatter chart: Value.", Label(Svg(points)), StringComparison.Ordinal);
        Assert.Equal("Chart. 2 data points, 1 to 2.", Label(Svg(nothing)));
    }

    [Fact]
    public void A_chart_mixing_bars_and_a_line_is_a_chart()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Sales)[Ui.ChartSvg[Ui.ChartBar.Field((Sale s) => s.North), Ui.ChartLine.Field((Sale s) => s.South)]],
            (606, 202)));

        var label = Label(Svg(html));

        Assert.StartsWith("Chart: Series 1, Series 2.", label, StringComparison.Ordinal);
    }

    [Fact]
    public void A_legend_entry_with_no_field_names_no_series_by_its_place()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Sales)[
                Ui.ChartSvg[Ui.ChartLine.Field((Sale s) => s.North), Ui.ChartLine.Field((Sale s) => s.South)],
                Ui.ChartLegend.Label("South"),
                Ui.ChartLegend.Label("North")
            ],
            (606, 202)));

        var label = Label(Svg(html));

        Assert.StartsWith("Line chart: Series 1, Series 2.", label, StringComparison.Ordinal);
    }

    [Fact]
    public void A_series_with_no_format_of_its_own_is_written_as_the_value_axis_writes_it()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Sales)[
                Ui.ChartSvg[
                    Ui.ChartLine.Field((Sale s) => s.North),
                    Ui.ChartAxis.Y.Format(new UiChartFormat { Style = Ui.ChartFormatStyle.Percent })
                ],
                Ui.ChartLegend.Field((Sale s) => s.North).Label("North")
            ],
            (606, 202)));

        var table = Table(html);

        Assert.Equal(("1", "1,000%"), Rows(table)[0]);
        Assert.EndsWith("Values from 1,000% to 1,200%.", Label(Svg(html)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_hungarian_reader_hears_the_chart_in_hungarian()
    {
        var html = KitCulture.In("hu", () => InEnglish(() => MeasuredCharts.Html(
            Ui.Chart.Value(Sales)[Ui.ChartSvg[Ui.ChartBar.Field((Sale s) => s.North), Ui.ChartBar.Field((Sale s) => s.South)]],
            (606, 202))));

        var table = Table(html);

        Assert.Equal("Oszlopdiagram: 1. sorozat, 2. sorozat. 3 adatpont, 1 – 3. Értékek: 10 – 22.", Label(Svg(html)));
        Assert.Equal(["Sor", "1. sorozat", "2. sorozat"], Headings(table));
    }

    // The drawing's opening tag.
    private static string Svg(string html) => Regex.Match(html, "<svg [^>]*>").Value;

    // Its accessible name, as a screen reader reads it.
    private static string Label(string svg) => System.Net.WebUtility.HtmlDecode(Regex.Match(svg, "aria-label=\"([^\"]*)\"").Groups[1].Value);

    private static string Table(string html) => Regex.Match(html, "<table class=\"sr-only\".*?</table>", RegexOptions.Singleline).Value;

    private static string[] Headings(string table) =>
        [.. Regex.Matches(table, "<th scope=\"col\">([^<]*)</th>").Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))];

    // Each row's heading and first value.
    private static (string Row, string Value)[] Rows(string table) =>
        [.. Regex.Matches(table, "<tr><th scope=\"row\">([^<]*)</th><td>([^<]*)</td>").Select(match =>
            (System.Net.WebUtility.HtmlDecode(match.Groups[1].Value), System.Net.WebUtility.HtmlDecode(match.Groups[2].Value)))];
}
