using Rask.Testing;

namespace Rask.UiTests.Components;

// Min and Max: the earliest and the latest moment the pair takes — the day picker's bounds, the time picker's on the
// first and the last day, and a day picked onto a bound day with a time past it brought back to the bound.
public sealed partial class UiDayAndTimeTests
{
    private static readonly DateTime Earliest = new(2026, 11, 6, 10, 0, 0);
    private static readonly DateTime Latest = new(2026, 11, 20, 16, 30, 0);

    private static Page Bounded(Visit visit) => Acting.Render(() =>
        Form.Model(visit)[Ui.DayAndTime.Bind(() => visit.At).Min(Earliest).Max(Latest).Locale("en-US").Label("At").Id("at")]);

    [Fact]
    public async Task A_day_picked_onto_the_first_day_with_an_earlier_time_takes_the_earliest_moment()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 9, 8, 15, 0) };
        var page = Bounded(visit);

        await page.On(Day).Change("2026-11-06");
        await Acting.Act(page);

        Assert.Equal(Earliest, visit.At);
    }

    [Fact]
    public async Task A_day_picked_onto_the_last_day_with_a_later_time_takes_the_latest_moment()
    {
        var visit = new Visit { At = new DateTime(2026, 11, 9, 18, 0, 0) };
        var page = Bounded(visit);

        await page.On(Day).Change("2026-11-20");
        await Acting.Act(page);

        Assert.Equal(Latest, visit.At);
    }

    [Fact]
    public async Task A_day_typed_before_the_first_day_is_not_taken()
    {
        var at = new DateTime(2026, 11, 9, 12, 0, 0);
        var visit = new Visit { At = at };
        var page = Bounded(visit);

        await page.On(Day).Change("2026-11-05");
        await Acting.Act(page);

        Assert.Equal(at, visit.At);
    }

    [Fact]
    public async Task A_time_typed_before_the_earliest_on_the_first_day_is_not_taken_and_one_on_another_day_is()
    {
        var first = new Visit { At = new DateTime(2026, 11, 6, 12, 0, 0) };
        var other = new Visit { At = new DateTime(2026, 11, 9, 12, 0, 0) };
        var onFirst = Bounded(first);
        var onOther = Bounded(other);

        await onFirst.On(Time).Change("09:00");
        await Acting.Act(onFirst);
        await onOther.On(Time).Change("09:00");
        await Acting.Act(onOther);

        Assert.Equal(new DateTime(2026, 11, 6, 12, 0, 0), first.At);
        Assert.Equal(new DateTime(2026, 11, 9, 9, 0, 0), other.At);
    }
}
