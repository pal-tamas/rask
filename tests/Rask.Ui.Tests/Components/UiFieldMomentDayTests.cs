using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Field.Bind</c> over a <c>DateTime</c> its shape says is a day — <c>[DataType(DataType.Date)]</c>, a
///     <see cref="FieldKind.Day" /> shape — draws a date picker alone, as for a <c>DateOnly</c>, and writes the moment
///     on the day picked at the time of day it had.
/// </summary>
public sealed partial class UiFieldMomentDayTests : global::Rask.Core.RaskMarkup
{
    private const string Day = "[data-ui-date-picker] > input[type=\"hidden\"]";

    static UiFieldMomentDayTests() =>
        RaskValidation.RegisterFieldShapes(typeof(Delivery), static name => name switch
        {
            nameof(Delivery.Arrived) => new FieldShape(FieldKind.Day, NotAfterToday: true),
            nameof(Delivery.Fueled) => new FieldShape(FieldKind.Moment) { Input = global::Rask.Core.InputType.Date },
            nameof(Delivery.Loaded) => new FieldShape(FieldKind.Moment),
            _ => null,
        });

    private static Page Field(Delivery delivery, System.Linq.Expressions.Expression<Func<DateTime?>> member) =>
        Acting.Render(() => Form.Model(delivery)[Ui.Field.Bind(member).Placeholder("Pick a day")]);

    [Fact]
    public void A_moment_typed_as_a_date_is_a_date_picker_alone_showing_its_day()
    {
        var shift = new Shift { Started = new DateTime(2025, 3, 14, 8, 30, 0, DateTimeKind.Unspecified) };

        var page = Page.Render(() => Form.Model(shift)[Ui.Field.Bind(() => shift.Started).Label("Started")]);

        Assert.Equal("2025-03-14", page.Find(Day).Attribute("value"));
        Assert.False(page.Exists("[data-ui-time-picker]"));
        Assert.False(page.Exists("input[type=\"date\"]"));
        Assert.True(page.Exists("label[for=\"f-started\"]"));
    }

    [Fact]
    public async Task A_day_picked_for_a_moment_keeps_the_time_of_day_it_had()
    {
        var delivery = new Delivery { Fueled = new DateTime(2025, 3, 14, 8, 30, 15, DateTimeKind.Unspecified) };
        var page = Field(delivery, () => delivery.Fueled);

        await page.On(Day).Change("2025-03-20");
        await Acting.Act(page);

        Assert.Equal(new DateTime(2025, 3, 20, 8, 30, 15, DateTimeKind.Unspecified), delivery.Fueled);
    }

    [Fact]
    public async Task A_first_day_picked_for_a_moment_held_as_a_day_is_its_midnight_and_ends_today()
    {
        var delivery = new Delivery();
        using var clock = Clock.Fake(at: new DateTimeOffset(2025, 3, 14, 9, 0, 0, TimeSpan.Zero));
        var page = Field(delivery, () => delivery.Arrived);
        var calendar = page.Find("[data-rask-calendar]").Attribute("data-rask-calendar");

        await page.On(Day).Change("2025-03-10");
        await Acting.Act(page);

        Assert.Contains("\"max\":\"2025-03-14\"", calendar, StringComparison.Ordinal);
        Assert.Contains("Pick a day", page.Html, StringComparison.Ordinal);
        Assert.Equal(new DateTime(2025, 3, 10), delivery.Arrived);
    }

    [Fact]
    public async Task A_day_taken_away_from_a_moment_that_may_hold_none_empties_it()
    {
        var delivery = new Delivery { Fueled = new DateTime(2025, 3, 14, 8, 30, 0) };
        var page = Field(delivery, () => delivery.Fueled);

        await page.On(Day).Change(string.Empty);
        await Acting.Act(page);

        Assert.Null(delivery.Fueled);
    }

    [Fact]
    public void A_moment_its_shape_says_nothing_more_of_is_still_a_day_and_a_time()
    {
        var delivery = new Delivery();

        var page = Field(delivery, () => delivery.Loaded);

        Assert.True(page.Exists("[data-ui-day-and-time] > [data-ui-time-picker]"));
    }

    // A model that registers no shapes: its members are read off their attributes.
    private sealed class Shift
    {
        [DataType(DataType.Date)]
        public DateTime Started { get; set; }
    }

    private sealed class Delivery
    {
        public DateTime? Arrived { get; set; }

        public DateTime? Fueled { get; set; }

        public DateTime? Loaded { get; set; }
    }
}
