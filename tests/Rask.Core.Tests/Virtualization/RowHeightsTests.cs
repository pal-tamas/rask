using Rask.Core.Virtualization;

namespace Rask.Core.Tests.Virtualization;

public class RowHeightsTests
{
    [Fact]
    public void A_row_is_the_estimate_until_a_window_has_been_measured()
    {
        var heights = new RowHeights();

        heights.From(32);

        Assert.Equal(32, heights.Average);
        Assert.Equal(0, heights.Measured);
    }

    [Fact]
    public void A_row_is_the_running_average_of_every_window_measured()
    {
        var heights = new RowHeights();
        heights.From(32);

        heights.Add(separation: 400, rows: 10);
        heights.Add(separation: 200, rows: 10);

        Assert.Equal(30, heights.Average);
        Assert.Equal(20, heights.Measured);
    }

    [Fact]
    public void A_new_estimate_forgets_what_was_measured_and_the_same_one_keeps_it()
    {
        var heights = new RowHeights();
        heights.From(32);
        heights.Add(separation: 800, rows: 10);

        heights.From(32);
        var kept = heights.Average;
        heights.From(48);

        Assert.Equal(80, kept);
        Assert.Equal(48, heights.Average);
        Assert.Equal(0, heights.Measured);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(400, 0)]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, 10)]
    public void A_window_of_no_rows_or_no_room_is_not_counted(double separation, int rows)
    {
        var heights = new RowHeights();
        heights.From(32);

        var counted = heights.Add(separation, rows);

        Assert.False(counted);
        Assert.Equal(32, heights.Average);
    }

    [Fact]
    public void No_row_is_taken_to_be_shorter_than_a_pixel_however_small_the_rows_measure()
    {
        var heights = new RowHeights();
        heights.From(32);

        heights.Add(separation: 0.01, rows: 1000);

        Assert.Equal(1, heights.Average);
    }
}
