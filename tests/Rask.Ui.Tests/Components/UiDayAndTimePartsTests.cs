using System.ComponentModel.DataAnnotations;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.DayAndTime.Bind(() =&gt; m.Day, () =&gt; m.Time)</c> (#1362): the day and the time bound to two members, each
///     saying its own messages under its own part.
/// </summary>
public sealed partial class UiDayAndTimePartsTests : global::Rask.Core.RaskMarkup
{
    private const string Day = "#at [data-rask-segments] input[type=\"hidden\"]";
    private const string Time = "[data-ui-time-picker] [data-rask-segments] input[type=\"hidden\"]";
    private const string TimeDisabled = "[data-ui-time-picker] input[data-rask-segment=\"hour\"][disabled]";
    private const string HalfTime = """{"type":"change","value":"","bad":"time"}""";

    private static Page Delivery(Note note, Action? saved = null) => Acting.Render(() =>
        Form.Model(note).OnSubmit(_ => saved?.Invoke())[
            Ui.DayAndTime.Bind(() => note.Day, () => note.Time).Locale("en-US").Label("Delivered").Id("at")
        ]);

    [Fact]
    public async Task A_pair_bound_to_a_day_and_a_time_writes_each_to_its_own_member()
    {
        var note = new Note { Day = new DateOnly(2026, 11, 6), Time = new TimeOnly(14, 35) };
        var page = Delivery(note);
        var drawn = (page.Find(Day).Attribute("value"), page.Find(Time).Attribute("value"));

        await page.On(Day).Change("2026-11-09");
        await page.On(Time).Change("09:05");
        await Acting.Act(page);

        Assert.Equal(("2026-11-06", "14:35"), drawn);
        Assert.Equal(new DateOnly(2026, 11, 9), note.Day);
        Assert.Equal(new TimeOnly(9, 5), note.Time);
    }

    [Fact]
    public async Task The_time_is_disabled_until_a_day_is_chosen_and_a_day_alone_leaves_the_time_empty()
    {
        var note = new Note();
        var page = Delivery(note);
        var before = page.Exists(TimeDisabled);

        await page.On(Day).Change("2026-11-06");
        var waiting = page.Exists(TimeDisabled);
        await Acting.Act(page);

        Assert.True(before);
        Assert.False(waiting);
        Assert.False(page.Exists(TimeDisabled));
        Assert.Equal(new DateOnly(2026, 11, 6), note.Day);
        Assert.Null(note.Time);
    }

    [Fact]
    public async Task Each_part_says_its_own_members_message_under_itself_so_the_pair_says_two()
    {
        var note = new Ruled { Day = new DateOnly(2026, 11, 8) };
        var saves = 0;
        var page = Acting.Render(() => Form.Model(note).OnSubmit(_ => saves++)[
            Ui.DayAndTime.Bind(() => note.Day, () => note.Time).Locale("en-US").Label("Delivered").Id("at")]);

        await page.On("form").Submit();

        Assert.Equal("No deliveries on a Sunday.", page.TextOf("#at-error"));
        Assert.Equal("The Time field is required.", page.TextOf("#at-time-error"));
        Assert.Equal("true", page.Find("#at").Attribute("aria-invalid"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_time_left_half_typed_says_so_under_the_time_and_not_under_the_day()
    {
        var note = new Note { Day = new DateOnly(2026, 11, 6), Time = new TimeOnly(14, 35) };
        var saves = 0;
        var page = Delivery(note, () => saves++);

        await page.On(Time).Raise("change", HalfTime);
        await page.On("form").Submit();

        Assert.Equal("Enter a valid time.", page.TextOf("#at-time-error"));
        Assert.Equal("", page.TextOf("#at-error"));
        Assert.Equal(new TimeOnly(14, 35), note.Time);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_day_taken_away_empties_the_time_and_disables_it_again()
    {
        var note = new Note { Day = new DateOnly(2026, 11, 6), Time = new TimeOnly(14, 35) };
        var page = Delivery(note);

        await page.On(Day).Change(string.Empty);
        await Acting.Act(page);

        Assert.Null(note.Day);
        Assert.Null(note.Time);
        Assert.True(page.Exists(TimeDisabled));
    }

    [Fact]
    public async Task A_live_pair_writes_both_members_as_they_are_chosen()
    {
        var note = new Note();
        var page = Page.Render(() => Form.Model(note)[
            Ui.DayAndTime.Bind(() => note.Day, () => note.Time).Live().Locale("en-US").Id("at")]);

        await page.On(Day).Change("2026-11-06");
        await page.On(Time).Change("16:00");

        Assert.Equal(new DateOnly(2026, 11, 6), note.Day);
        Assert.Equal(new TimeOnly(16, 0), note.Time);
    }

    [Fact]
    public async Task A_pair_keyed_after_its_two_members_still_binds_them()
    {
        var note = new Note { Day = new DateOnly(2026, 11, 6) };
        var page = Page.Render(() => Form.Model(note)[
            Ui.DayAndTime.Bind(() => note.Day, () => note.Time).Key("delivery").Live().Locale("en-US").Id("at")]);

        await page.On(Time).Change("08:30");

        Assert.Equal(new TimeOnly(8, 30), note.Time);
    }

    [Fact]
    public async Task After_bind_hears_the_moment_the_day_and_the_time_make()
    {
        var note = new Note { Day = new DateOnly(2026, 11, 6), Time = new TimeOnly(14, 35) };
        var heard = new List<DateTime?>();
        var page = Page.Render(() => Form.Model(note)[
            Ui.DayAndTime.Bind(() => note.Day, () => note.Time).Live().AfterBind(heard.Add).Locale("en-US").Id("at")]);

        await page.On(Time).Change("16:00");

        Assert.Equal([new DateTime(2026, 11, 6, 16, 0, 0)], heard);
    }

    private sealed class Note
    {
        public DateOnly? Day { get; set; }

        public TimeOnly? Time { get; set; }
    }

    private sealed class Ruled
    {
        [NoSunday]
        public DateOnly? Day { get; set; }

        [Required]
        public TimeOnly? Time { get; set; }
    }

    // A rule of the day alone, as a delivery note's: no deliveries on a Sunday.
    [AttributeUsage(AttributeTargets.Property)]
    private sealed class NoSundayAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value) => value is not DateOnly { DayOfWeek: DayOfWeek.Sunday };

        public override string FormatErrorMessage(string name) => "No deliveries on a Sunday.";
    }
}
