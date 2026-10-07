namespace Rask.UiTests.Components;

/// <summary><c>UiDateRange</c> — Flux UI's <c>DateRange</c>: what a range holds and what each preset means.</summary>
public class UiDateRangeTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static DateOnly Jan(int day) => new(2026, 1, day);

    [Fact]
    public void A_range_counts_and_contains_both_its_ends()
    {
        var range = new UiDateRange(Jan(10), Jan(13));

        var count = range.Count;

        Assert.Equal(4, count);
        Assert.True(range.Contains(Jan(10)));
        Assert.True(range.Contains(Jan(13)));
        Assert.False(range.Contains(Jan(14)));
    }

    [Fact]
    public void An_unset_range_holds_nothing()
    {
        var unset = default(UiDateRange);

        var count = unset.Count;

        Assert.Equal(0, count);
        Assert.False(unset.Contains(default));
        Assert.Null(unset.Preset);
    }

    [Fact]
    public void Between_orders_two_days_whichever_was_picked_first()
    {
        var forwards = UiDateRange.Between(Jan(3), Jan(9));

        var backwards = UiDateRange.Between(Jan(9), Jan(3));

        Assert.Equal(new UiDateRange(Jan(3), Jan(9)), forwards);
        Assert.Equal(forwards, backwards);
    }

    [Theory]
    [InlineData(Ui.DateRangePreset.Today, "2026-01-15", "2026-01-15")]
    [InlineData(Ui.DateRangePreset.Yesterday, "2026-01-14", "2026-01-14")]
    [InlineData(Ui.DateRangePreset.ThisWeek, "2026-01-11", "2026-01-17")]
    [InlineData(Ui.DateRangePreset.Last7Days, "2026-01-09", "2026-01-15")]
    [InlineData(Ui.DateRangePreset.ThisMonth, "2026-01-01", "2026-01-31")]
    [InlineData(Ui.DateRangePreset.YearToDate, "2026-01-01", "2026-01-15")]
    [InlineData(Ui.DateRangePreset.AllTime, "2012-01-01", "2026-01-15")]
    [InlineData(Ui.DateRangePreset.LastMonth, "2025-12-01", "2025-12-31")]
    [InlineData(Ui.DateRangePreset.NextQuarter, "2026-04-01", "2026-06-30")]
    public void A_preset_is_the_range_flux_gives_it_on_the_same_day(Ui.DateRangePreset preset, string start, string end)
    {
        var min = new DateOnly(2012, 1, 1);

        var range = UiDateRange.Of(preset, Today, DayOfWeek.Sunday, min);

        Assert.Equal(DateOnly.ParseExact(start, "yyyy-MM-dd"), range.Start);
        Assert.Equal(DateOnly.ParseExact(end, "yyyy-MM-dd"), range.End);
        Assert.Equal(preset, range.Preset);
    }

    [Fact]
    public void A_week_preset_follows_the_first_day_it_is_given()
    {
        var sunday = UiDateRange.Of(Ui.DateRangePreset.ThisWeek, Today);

        var monday = UiDateRange.Of(Ui.DateRangePreset.ThisWeek, Today, DayOfWeek.Monday);

        Assert.Equal(Jan(11), sunday.Start);
        Assert.Equal(Jan(12), monday.Start);
        Assert.Equal(Jan(18), monday.End);
    }
}
