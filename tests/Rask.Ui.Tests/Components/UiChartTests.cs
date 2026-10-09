using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's chart, drawn in C#: <c>Ui.Chart.Value(rows)[Ui.ChartSvg[Ui.ChartLine.Field(…), Ui.ChartAxis.X…]]</c>.
/// </summary>
/// <remarks>
///     What is pinned here is what Flux's own layout was measured to do — the round ticks, the gutters set by the
///     widest label, the curve through the points — as numbers, since a chart that is a pixel out is a chart that
///     misstates its data. <c>scripts/flux/parity-chart.mjs</c> holds the same numbers to Flux's live page.
/// </remarks>
public partial class UiChartTests : global::Rask.Core.RaskMarkup
{
    private sealed record Visit(DateOnly Day, int Visitors, decimal Revenue);

    private static readonly Visit[] Visits =
    [
        .. new[] { 171, 227, 269, 223, 249, 251, 300, 246, 162, 115, 176, 144, 184, 269, 259, 267 }
            .Select((visitors, day) => new Visit(new DateOnly(2026, 9, 22).AddDays(day), visitors, visitors * 10.5m)),
    ];

    private static string InEnglish(Func<string> render)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static string Intro() => InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Value(Visits)[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Visit v) => v.Visitors).Class("text-pink-500"),
                Ui.ChartAxis.X.Field((Visit v) => v.Day)[Ui.ChartAxisLine, Ui.ChartAxisTick],
                Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                Ui.ChartCursor
            ],
            Ui.ChartTooltip[
                Ui.ChartTooltipHeading.Field((Visit v) => v.Day),
                Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")
            ]
        ],
        (606, 202)));

    private static string Bare(params Rask.Core.Component[] parts) => InEnglish(() => MeasuredCharts.Html(Ui.Chart.Value([0, 10, 20])[Ui.ChartSvg.Gutter("0")[parts]], (200, 100)));

    [Theory]
    [InlineData(0, 281, 0, 281, "0 100 200")]
    [InlineData(0, 297, 0, 300, "0 100 200 300")]
    [InlineData(0, 289, 0, 289, "0 100 200")]
    [InlineData(0, 290, 0, 300, "0 100 200 300")]
    [InlineData(0, 95, 0, 95, "0 20 40 60 80")]
    [InlineData(0, 1150, 0, 1150, "0 200 400 600 800 1000")]
    [InlineData(0, 1200, 0, 1200, "0 500 1000")]
    [InlineData(200, 210, 0, 210, "0 50 100 150 200")]
    [InlineData(-47, 120, -50, 120, "-50 0 50 100")]
    [InlineData(-300, -20, -300, -20, "-300 -200 -100")]
    [InlineData(0, 0, 0, 1, "0 0.2 0.4 0.6 0.8 1")]
    public void The_value_axis_starts_at_zero_and_steps_by_a_round_quarter_of_its_span(double min, double max, double low, double high, string ticks)
    {
        var scale = UiChartValueScale.For(min, max, axis: null);

        var written = string.Join(' ', scale.Ticks.Select(tick => tick.ToString(CultureInfo.InvariantCulture)));

        Assert.Equal((low, high), (scale.Low, scale.High));
        Assert.Equal(ticks, written);
    }

    [Fact]
    public void An_axis_told_its_ends_and_its_tick_count_keeps_them()
    {
        var axis = Ui.ChartAxis.Y.TickStart(0).TickEnd(1).TickCount(3);

        var scale = UiChartValueScale.For(0.09, 0.59, axis);

        Assert.Equal((0d, 1d), (scale.Low, scale.High));
        Assert.Equal([0, 0.5, 1], scale.Ticks);
    }

    [Theory]
    [InlineData(20, 21, 21, "h:mm tt")]             // a row a minute: a tick a minute
    [InlineData(20, 7, 7, "h:mm tt")]               // a row every 3m20s: a tick every 3 minutes
    [InlineData(8 * 60, 48, 9, "h tt")]             // ten minutes apart over eight hours: hourly
    [InlineData(3 * 24 * 60, 7, 7, "ddd h tt")]     // twelve hours apart over three days
    [InlineData(15 * 24 * 60, 16, 16, "MMM d")]     // daily
    [InlineData(400 * 24 * 60, 7, 7, "MMM")]        // every other month
    [InlineData(4 * 366 * 24 * 60, 5, 5, "yyyy")]   // yearly
    public void A_time_axis_ticks_at_the_pace_of_its_rows_and_is_written_for_its_reach(int minutes, int rows, int ticks, string pattern)
    {
        var first = new DateTime(2026, 3, 10, 9, 17, 23, DateTimeKind.Unspecified);

        var drawn = UiChartTimeTicks.For(first, first.AddMinutes(minutes), rows);

        Assert.Equal(ticks, drawn.Count);
        Assert.Equal(first, drawn[0]);
        Assert.Equal(pattern, UiChartTimeTicks.Pattern(drawn[0], drawn[^1]));
    }

    [Fact]
    public void Ticks_after_the_first_fall_on_whole_units_counted_from_the_first_rows_own()
    {
        var first = new DateTime(2026, 3, 10, 9, 17, 23, DateTimeKind.Unspecified);

        var drawn = UiChartTimeTicks.For(first, first.AddMinutes(20), rows: 7);

        Assert.Equal(new DateTime(2026, 3, 10, 9, 20, 0, DateTimeKind.Unspecified), drawn[1]);
        Assert.Equal(new DateTime(2026, 3, 10, 9, 35, 0, DateTimeKind.Unspecified), drawn[^1]);
    }

    [Theory]
    [InlineData("300", false, 22.5625)]
    [InlineData("100", false, 20.03125)]
    [InlineData("$1,000", false, 38.453125)]
    [InlineData("Sep 22", true, 40.21875)]
    [InlineData("5:49 AM", true, 48.96875)]
    public void A_label_is_as_wide_as_the_browser_measures_it_in_Inter(string label, bool medium, double width)
    {
        var measured = UiChartInter.Measure(label, medium);

        Assert.Equal(width, measured);
    }

    [Fact]
    public void The_plot_is_pulled_in_by_the_widest_value_label_and_the_labels_hanging_under_it()
    {
        var html = Intro();

        // 8 of gutter + one em + "300" (22.5625) on the left; 8 + one em + a 15px line of text at the bottom.
        Assert.Contains("<g transform=\"translate(42.5625, 167)\">", html, StringComparison.Ordinal);
        Assert.Contains("x1=\"42.5625\" x2=\"598\" y1=\"167\" y2=\"167\"", html, StringComparison.Ordinal);
        // The top tick's label is centred on its line, so the plot's top is half a label under the gutter.
        Assert.Contains("x1=\"42.5625\" x2=\"598\" y1=\"15.5\" y2=\"15.5\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Dates_that_would_touch_drop_every_other_label_and_a_hidden_last_one_lets_the_plot_reach_the_edge()
    {
        var html = Intro();

        Assert.Equal(8, Regex.Matches(html, "style=\"display: none;\"").Count);
        Assert.Contains("<g transform=\"translate(598, 167)\" style=\"display: none;\">", html, StringComparison.Ordinal);
        Assert.Matches(">Sep 22</text>", html);
    }

    [Fact]
    public void Names_that_would_touch_turn_instead_and_the_plot_makes_room_under_them()
    {
        var rows = Enumerable.Range(0, 14).Select(i => new Visit(default, i, i)).ToList();

        var html = InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Value(rows)[
            Ui.ChartSvg[
                Ui.ChartLine.Field((Visit v) => v.Visitors),
                Ui.ChartAxis.X.Field((Visit v) => "Row " + v.Visitors)[Ui.ChartAxisTick]
            ]
        ],
        (606, 202)));

        Assert.Equal(14, Regex.Matches(html, "style=\"transform: rotate\\(-45deg\\); text-anchor: end;\"").Count);
        Assert.DoesNotContain(", 167)", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_smooth_line_is_a_monotone_curve_with_its_handles_a_third_of_the_way_along()
    {
        var html = Bare(Ui.ChartLine);

        Assert.Contains(
            "d=\"M0,100C33.333333333333336,83.33333333333333 66.66666666666666,66.66666666666667 100,50"
            + "C133.33333333333334,33.33333333333333 166.66666666666666,16.666666666666668 200,0\"",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_straight_line_is_written_as_curves_whose_handles_lie_on_their_ends()
    {
        var html = Bare(Ui.ChartLine.Curve(Ui.ChartCurve.None));

        Assert.Contains("d=\"M 0 100 C 0 100, 100 50, 100 50 C 100 50, 200 0, 200 0\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_area_is_its_line_closed_down_to_the_baseline()
    {
        var html = Bare(Ui.ChartArea.Curve(Ui.ChartCurve.None));

        Assert.Contains("<path fill=\"currentColor\" d=\"M 0 100 C 0 100, 100 50, 100 50 C 100 50, 200 0, 200 0 L200,100 L0,100 Z\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bar_fills_nine_tenths_of_its_band_and_rounds_its_value_end_by_eight()
    {
        var html = InEnglish(() => MeasuredCharts.Html(Ui.Chart.Value([10, 20])[Ui.ChartSvg.Gutter("0")[Ui.ChartBar]], (200, 100)));

        Assert.Contains("d=\"M5,58A8,8 0 0 1 13,50H87A8,8 0 0 1 95,58V100H5V58Z\"", html, StringComparison.Ordinal);
        Assert.Contains("d=\"M105,8A8,8 0 0 1 113,0H187A8,8 0 0 1 195,8V100H105V8Z\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stack_stands_each_bar_on_the_one_before_and_the_axis_reaches_their_total()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Value(Visits.Take(1))[
            Ui.ChartSvg.Gutter("0")[
                Ui.ChartStack.Width("100%")[
                    Ui.ChartBar.Field((Visit v) => v.Visitors),
                    Ui.ChartBar.Field((Visit v) => v.Visitors)
                ]
            ]
        ],
        (100, 100)));

        Assert.Contains("d=\"M0,50 h100 v50 h-100 v-50\"", html, StringComparison.Ordinal);
        Assert.Contains("d=\"M0,0 h100 v50 h-100 v-50\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pie_turns_clockwise_from_twelve_inside_its_gutter_and_half_its_stroke()
    {
        var html = InEnglish(() => MeasuredCharts.Html(Ui.Chart.Value([50, 50])[Ui.ChartSvg[Ui.ChartPie]], (219, 219)));

        // Radius 109.5 - 8 - 1.5 = 100, about (109.5, 109.5): the first slice is the right-hand half, and half a
        // turn's sine is not quite zero in floating point — Flux's own paths carry the same stray digit.
        Assert.Contains("class=\"fill-sky-500 stroke-white dark:stroke-zinc-900\" d=\"M109.5,9.5 A100,100 0 0 1 109.50000000000001,209.5 L109.5,109.5 Z\"", html, StringComparison.Ordinal);
        Assert.Contains("fill-lime-500", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_donut_takes_its_hole_as_a_share_of_its_radius_and_a_row_can_choose_its_hue()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Value(Visits.Take(2))[
            Ui.ChartSvg[
                Ui.ChartPie.Field((Visit v) => v.Visitors).ColorField((Visit v) => Ui.Color.Amber).InnerRadius("60%")
            ]
        ],
        (219, 219)));

        Assert.Contains("fill-amber-500", html, StringComparison.Ordinal);
        Assert.Contains("A60,60 0 ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chart_asks_for_the_plot_hook_and_its_one_tooltip_rests_in_the_document_unseen()
    {
        var html = Intro();

        var tooltips = Regex.Count(html, "data-rask-plot-tooltip");

        Assert.StartsWith("<div class=\"relative block\" data-ui-chart data-rask-plot=\"\">", html, StringComparison.Ordinal);
        Assert.Equal(1, tooltips);
        Assert.Matches("<div class=\"pointer-events-none absolute flex [^\"]* opacity-0 [^\"]*data-active:opacity-100[^\"]*\" data-rask-plot-tooltip=\"15\">", html);
        Assert.Contains("<path fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\" stroke-dasharray=\"4,4\" class=\"text-zinc-500 dark:text-zinc-300\" opacity=\"0\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plot_area_says_where_every_row_is_and_holds_the_one_cursor_the_hook_moves()
    {
        var html = Intro();

        var area = Regex.Match(html, "<div class=\"absolute\" style=\"([^\"]*)\" data-rask-plot-area=\"([^\"]*)\">(.*?)</div></div>");
        var places = area.Groups[2].Value.Split(' ');

        // The plot of the 606 by 202 box, in shares of it: in from the left by the widest label, 15.5 down, to 598 and 167.
        Assert.Equal("left:7.024%;top:7.673%;width:91.656%;height:75%", area.Groups[1].Value);
        Assert.Equal(16, places.Length);
        Assert.Equal(["0", "0.06667", "1"], [places[0], places[1], places[^1]]);
        Assert.Equal(1, Regex.Count(area.Groups[3].Value, "<div"));
        Assert.Contains("in-data-active:block", area.Groups[3].Value, StringComparison.Ordinal);
        Assert.Contains("style=\"left:calc(var(--rask-plot-at, 0) * 100%)\"", area.Groups[3].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tooltip_part_carries_what_it_reads_for_every_row_and_nothing_at_rest()
    {
        var html = Intro();

        var heading = Regex.Match(html, "border-b [^>]* data-rask-plot-text=\"([^\"]*)\"></div>").Groups[1].Value.Split("&#xA;");
        var value = Regex.Match(html, "<div class=\"grow\"></div><div data-rask-plot-text=\"([^\"]*)\"></div>").Groups[1].Value.Split("&#xA;");

        Assert.Equal(16, heading.Length);
        Assert.Equal(["9/22/2026", "9/28/2026", "10/7/2026"], [heading[0], heading[6], heading[^1]]);
        Assert.Equal(["171", "300", "267"], [value[0], value[6], value[^1]]);
    }

    [Fact]
    public void An_area_cursor_is_as_wide_as_a_rows_band_and_a_horizontal_chart_runs_its_rows_down()
    {
        var upright = InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Value(Visits)[
            Ui.ChartSvg[Ui.ChartBar.Field((Visit v) => v.Visitors).Width("85%"), Ui.ChartCursor.Type(Ui.ChartCursorType.Area)]],
        (606, 202)));
        var lying = InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Horizontal().Value(Visits)[
            Ui.ChartSvg[Ui.ChartBar.Field((Visit v) => v.Visitors), Ui.ChartCursor]],
        (606, 202)));

        var places = Regex.Match(upright, "data-rask-plot-area=\"([^\"]*)\"").Groups[1].Value.Split(' ');

        // Sixteen bands: the cursor covers a whole one, whatever the bar fills, centred on the row in its middle.
        Assert.Contains("style=\"left:calc(var(--rask-plot-at, 0) * 100%);width:6.25%\"", upright, StringComparison.Ordinal);
        Assert.Equal(["0.03125", "0.96875"], [places[0], places[^1]]);
        Assert.DoesNotContain("data-rask-plot-axis", upright, StringComparison.Ordinal);
        Assert.Contains("data-rask-plot-axis=\"y\"", lying, StringComparison.Ordinal);
        Assert.Contains("style=\"top:calc(var(--rask-plot-at, 0) * 100%)\"", lying, StringComparison.Ordinal);
    }

    [Fact]
    public void A_drawing_says_the_box_it_was_drawn_for_and_draws_nothing_before_it_is_told_one()
    {
        var measured = Bare(Ui.ChartLine);
        var unmeasured = InEnglish(() => Ui.Chart.Value([0, 10, 20])[Ui.ChartSvg[Ui.ChartLine]].ToHtml());
        var pie = InEnglish(() => MeasuredCharts.Html(Ui.Chart.Value([1, 2])[Ui.ChartSvg[Ui.ChartPie]], (180.671875, 180.671875)));

        var layers = new[] { measured, unmeasured, pie }.Select(html => Regex.Match(html, "data-ui-chart-hover data-rask-measure=\"\" data-rask-plot-frame=\"\"><input type=\"hidden\" value=\"([^\"]*)\"").Groups[1].Value).ToArray();

        // What the runtime's hook would write for the same box, so a chart is not told a box it was drawn for; and
        // an empty field until then, which is no box: the hook answers it with the real one. As Flux's chart, an
        // empty box of the right size until it has been measured.
        Assert.Equal(["200 100", "", "180.67 180.67"], layers);
        Assert.DoesNotContain("<svg", unmeasured, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-plot-area", pie, StringComparison.Ordinal);
    }

    [Fact]
    public void Points_and_slices_name_their_row_and_a_pies_tooltip_has_a_dot_per_slice()
    {
        var points = Bare(Ui.ChartPoint);
        var pie = InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Value(Visits.Take(3).ToArray())[
            Ui.ChartSvg[Ui.ChartPie.Field((Visit v) => v.Visitors).LabelField((Visit v) => v.Day)],
            Ui.ChartTooltip[Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).LabelField((Visit v) => v.Day)[Ui.ChartTooltipIndicator]]],
        (200, 200)));

        var slices = Regex.Matches(pie, "<path [^>]*data-rask-plot-row=\"(\\d)\"></path>").Select(match => match.Groups[1].Value);
        var dots = Regex.Matches(pie, "<div class=\"size-2.5 rounded-full hidden data-active:block (bg-[a-z]+-500)\" data-ui-chart-hover data-rask-plot-row=\"(\\d)\"").Select(match => match.Groups[1].Value + match.Groups[2].Value);

        Assert.Equal(3, Regex.Count(points, "<circle [^>]*data-rask-plot-row=\"[012]\"></circle>"));
        Assert.Equal(["0", "1", "2"], slices);
        Assert.Equal(["bg-sky-5000", "bg-lime-5001", "bg-orange-5002"], dots);
        Assert.Contains("<div class=\"size-2.5 rounded-full in-data-active:hidden\" data-rask-key=\"-1\"></div>", pie, StringComparison.Ordinal);
        Assert.Contains("data-rask-plot-text=\"9/22/2026&#xA;9/23/2026&#xA;9/24/2026\"", pie, StringComparison.Ordinal);
    }

    [Fact]
    public void A_summary_value_shows_the_latest_row_and_its_fallback_when_there_is_none()
    {
        static UiChartSummaryValue Revenue() =>
            Ui.ChartSummaryValue.Field((Visit v) => v.Revenue).Format(new() { Style = Ui.ChartFormatStyle.Currency, Currency = "USD" }).Fallback("n/a");

        var latest = InEnglish(() => Ui.Chart.Value(Visits)[Ui.ChartSummary[Revenue()]].ToHtml());
        var none = InEnglish(() => Ui.Chart.Value(Array.Empty<Visit>())[Ui.ChartSummary[Revenue()]].ToHtml());

        Assert.Matches("<span><slot data-rask-plot-text=\"\\$1,795\\.50&#xA;[^\"]*&#xA;\\$2,803\\.50\">\\$2,803\\.50</slot></span>", latest);
        Assert.Contains("<span><slot data-rask-plot-text=\"\">n/a</slot></span>", none, StringComparison.Ordinal);
    }

    [Fact]
    public void A_legend_entry_is_its_indicator_and_its_label()
    {
        var html = Ui.ChartLegend.Label("Visitors")[Ui.ChartLegendIndicator.Class("bg-blue-400")].ToHtml();

        Assert.Equal(
            "<div class=\"flex items-center gap-2 p-2\"><div class=\"size-2.5 rounded-full bg-blue-400\"></div>"
            + "<div class=\"text-xs text-zinc-500 dark:text-zinc-400\">Visitors</div></div>",
            html);
    }

    [Fact]
    public void A_horizontal_chart_puts_its_rows_down_the_side_and_its_values_along_the_bottom()
    {
        var html = InEnglish(() => MeasuredCharts.Html(
        Ui.Chart.Horizontal().Value(Visits.Take(2))[
            Ui.ChartSvg[
                Ui.ChartBar.Field((Visit v) => v.Visitors).Radius("0").Width("50%"),
                Ui.ChartAxis.Y.Field((Visit v) => v.Visitors)[Ui.ChartAxisTick],
                Ui.ChartAxis.X[Ui.ChartAxisTick]
            ]
        ],
        (200, 100)));

        Assert.Matches("text-anchor=\"end\" dx=\"-1em\"[^>]*>171</text>", html);
        Assert.Matches("text-anchor=\"middle\"[^>]*dy=\"1em\"[^>]*>200</text>", html);
    }

    [Theory]
    [InlineData(1234.56, Ui.ChartFormatStyle.Currency, "USD", null, "$1,234.56")]
    [InlineData(1234.56, Ui.ChartFormatStyle.Currency, "EUR", null, "€1,234.56")]
    [InlineData(0.85, Ui.ChartFormatStyle.Percent, null, null, "85%")]
    [InlineData(50, Ui.ChartFormatStyle.Unit, null, "megabyte", "50 MB")]
    [InlineData(1234567, Ui.ChartFormatStyle.Decimal, null, null, "1,234,567")]
    public void A_number_is_written_as_Intl_would_write_it(double value, Ui.ChartFormatStyle style, string? currency, string? unit, string written)
    {
        var format = new UiChartFormat { Style = style, Currency = currency, Unit = unit };

        var text = InEnglish(() => format.Number(value));

        Assert.Equal(written, text);
    }

    [Fact]
    public void Compact_and_scientific_notation_and_fraction_digits_shorten_a_number()
    {
        var compact = new UiChartFormat { Notation = Ui.ChartFormatNotation.Compact };
        var tenth = new UiChartFormat { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 };
        var fixedTwo = new UiChartFormat { MaximumFractionDigits = 2 };

        var written = InEnglish(() => string.Join(' ', compact.Number(1_000_000), compact.Number(1234), tenth.Number(3149), tenth.Number(0), fixedTwo.Number(3.1415926535)));

        Assert.Equal("1M 1.2K 3.1K 0 3.14", written);
    }

    [Fact]
    public void A_date_is_written_from_the_parts_asked_for()
    {
        var moment = new DateTime(2024, 3, 15, 14, 30, 0, DateTimeKind.Unspecified);
        UiChartFormat[] formats =
        [
            new() { Month = Ui.ChartFormatPart.Long, Day = Ui.ChartFormatPart.Numeric },
            new() { Month = Ui.ChartFormatPart.Short, Day = Ui.ChartFormatPart.Numeric },
            new() { Hour = Ui.ChartFormatPart.Numeric, Minute = Ui.ChartFormatPart.Numeric, Hour12 = true },
            new() { Hour = Ui.ChartFormatPart.TwoDigit, Minute = Ui.ChartFormatPart.TwoDigit, Hour12 = false },
            new() { Weekday = Ui.ChartFormatPart.Long },
            new() { Year = Ui.ChartFormatPart.Numeric },
            new() { Year = Ui.ChartFormatPart.Numeric, Month = Ui.ChartFormatPart.Numeric, Day = Ui.ChartFormatPart.Numeric },
            new() { DateStyle = Ui.ChartFormatLength.Full },
        ];

        var written = InEnglish(() => string.Join(" | ", formats.Select(format => format.Date(moment))));

        Assert.Equal("March 15 | Mar 15 | 2:30 PM | 14:30 | Friday | 2024 | 3/15/2024 | Friday, March 15, 2024", written);
    }

    [Fact]
    public void A_part_whose_lambda_reads_another_row_type_says_so()
    {
        var chart = Ui.Chart.Value(Visits)[Ui.ChartSvg[Ui.ChartLine.Field((string s) => s.Length)]];

        var thrown = Record.Exception(() => MeasuredCharts.Html(chart, (200, 100)));

        Assert.Contains("Give the lambda the chart's row type", thrown?.ToString(), StringComparison.Ordinal);
    }
}
