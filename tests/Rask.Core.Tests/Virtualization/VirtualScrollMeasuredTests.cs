using System.Text.Json;
using Rask.Core.Virtualization;

namespace Rask.Core.Tests.Virtualization;

public class VirtualScrollMeasuredTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(400)]
    [InlineData(1999)]
    [InlineData(19_000)]
    public void With_nothing_measured_the_measured_maths_draws_the_rows_the_fixed_maths_does(int scrollTop)
    {
        var scroll = new VirtualScroll();
        scroll.Read(Scrolled($$"""{"scrollTop":{{scrollTop}},"clientHeight":100,"scrollHeight":20000}"""));

        var (first, last) = scroll.Shown(1000, 20, 100, default, 20);
        var measured = VirtualScroll.Around(first, last, 3, 1000, 20);
        var fixedWindow = scroll.Window(20, 3, 1000, 100);

        Assert.Equal(fixedWindow, measured);
    }

    [Fact]
    public void Inside_the_drawn_window_a_row_is_found_where_the_browser_measured_it_to_stand()
    {
        // Rows 10..19 were drawn below a 200px spacer (20px a row) and measured 60px each: 600px together. The box's
        // top is 300px into them, so on the sixth drawn row (15); its 100px reach row 16's bottom part.
        var scroll = new VirtualScroll();
        var drawn = new VirtualWindow(10, 10, 200, 1800);
        scroll.Read(Scrolled("""{"scrollTop":500,"clientHeight":100,"scrollHeight":2600,"raskVirtual":[0,600,10,10,-1,-1,-1]}"""));

        var (first, last) = scroll.Shown(100, 20, 100, drawn, 20);

        Assert.Equal(15, first);
        Assert.Equal(17, last);
    }

    [Fact]
    public void The_rows_the_browser_says_the_box_s_edges_cut_win_over_the_window_s_average()
    {
        // The same window as above, but its rows are of mixed heights: the browser says the top edge cuts row 13 and the
        // bottom edge row 18, where the window's average would have said 15 and 16. Two windows each worked out from
        // their own average would each find the other, for ever.
        var scroll = new VirtualScroll();
        var drawn = new VirtualWindow(10, 10, 200, 1800);
        scroll.Read(Scrolled("""{"scrollTop":500,"clientHeight":100,"scrollHeight":2600,"raskVirtual":[0,600,10,10,-1,13,18]}"""));

        var (first, last) = scroll.Shown(100, 20, 100, drawn, 20);

        Assert.Equal(13, first);
        Assert.Equal(19, last);
    }

    [Fact]
    public void A_cut_row_outside_the_drawn_window_is_not_believed()
    {
        var scroll = new VirtualScroll();
        var drawn = new VirtualWindow(10, 10, 200, 1800);
        scroll.Read(Scrolled("""{"scrollTop":500,"clientHeight":100,"scrollHeight":2600,"raskVirtual":[0,600,10,10,-1,90000,4]}"""));

        var (first, last) = scroll.Shown(100, 20, 100, drawn, 20);

        Assert.Equal(15, first);
        Assert.Equal(17, last);
    }

    [Fact]
    public void A_box_scrolled_to_the_end_of_its_spacer_below_shows_the_last_rows()
    {
        var scroll = new VirtualScroll();
        var drawn = new VirtualWindow(0, 10, 0, 1800);
        scroll.Read(Scrolled("""{"scrollTop":2300,"clientHeight":100,"scrollHeight":2400,"raskVirtual":[0,600,10,0,-1,-1,-1]}"""));

        var (first, last) = scroll.Shown(100, 20, 100, drawn, 20);

        Assert.Equal(95, first);
        Assert.Equal(100, last);
        Assert.True(scroll.AtEnd);
    }

    [Fact]
    public void Content_above_the_list_inside_the_box_does_not_count_as_rows()
    {
        // A 40px header above the spacers: a box scrolled 40px is still at the first row.
        var scroll = new VirtualScroll();
        scroll.Read(Scrolled("""{"scrollTop":40,"clientHeight":100,"scrollHeight":2000,"raskVirtual":[40,200,10,0,-1,-1,-1]}"""));

        var (first, _) = scroll.Shown(100, 20, 100, new VirtualWindow(0, 10, 0, 1800), 20);

        Assert.Equal(0, first);
    }

    [Fact]
    public void A_report_of_another_window_than_the_one_drawn_is_read_at_the_average()
    {
        var scroll = new VirtualScroll();
        scroll.Read(Scrolled("""{"scrollTop":500,"clientHeight":100,"scrollHeight":2600,"raskVirtual":[0,600,10,40,-1,-1,-1]}"""));

        var (first, _) = scroll.Shown(100, 20, 100, new VirtualWindow(10, 10, 200, 1800), 20);

        Assert.Equal(25, first);
    }

    [Fact]
    public void The_window_around_rows_clamps_to_the_rows_there_are_whatever_the_overscan()
    {
        var past = VirtualScroll.Around(150, 170, int.MaxValue, 100, 20);
        var before = VirtualScroll.Around(-50, -10, 3, 100, 20);

        Assert.Equal(new VirtualWindow(0, 100, 0, 0), past);
        Assert.Equal(new VirtualWindow(0, 3, 0, 1940), before);
    }

    [Fact]
    public void The_spacers_are_the_rows_not_drawn_at_the_average_rounded_to_a_pixel()
    {
        var window = VirtualScroll.Around(10, 20, 2, 100, 33.4);

        Assert.Equal(new VirtualWindow(8, 14, 267, 2605), window);
    }

    [Theory]
    [InlineData("""{"raskVirtual":[0,600,10,0,-1]}""")]
    [InlineData("""{"raskVirtual":[0,"600",10,0,-1,-1,-1]}""")]
    [InlineData("""{"raskVirtual":{"a":1}}""")]
    [InlineData("""{"scrollTop":5}""")]
    public void A_report_that_is_not_seven_numbers_is_no_report(string target)
    {
        using var doc = JsonDocument.Parse(target);

        var report = VirtualReport.Read(doc.RootElement);

        Assert.Null(report);
    }

    [Fact]
    public void A_report_s_numbers_are_bounded_so_no_sum_of_them_overflows()
    {
        using var doc = JsonDocument.Parse("""{"raskVirtual":[-5,1e300,1e12,-3,-9,-4,1e20]}""");

        var report = VirtualReport.Read(doc.RootElement);

        Assert.Equal(new VirtualReport(0, int.MaxValue / 4.0, int.MaxValue / 2, 0, -1, -1, int.MaxValue / 2), report);
    }

    private static Event Scrolled(string target)
    {
        using var doc = JsonDocument.Parse("{\"target\":" + target + "}");
        return new Event(doc.RootElement);
    }
}
