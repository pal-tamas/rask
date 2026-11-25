using System.ComponentModel.DataAnnotations;
using Rask.Testing;
using Rask.Values;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.DatePicker</c> typed unless told otherwise, and <c>Ui.DatePicker.Bind(…)</c> deciding by the bound member's
///     type: a <c>DateTime</c> or <c>DateTime?</c> held as its day keeps its time of day on a new day, and a
///     <see cref="Day" /> or <see cref="Moment" /> value object is made with its own <c>Create</c>.
/// </summary>
public sealed partial class UiDatePickerBindTests : global::Rask.Core.RaskMarkup
{
    private const string Typed = "#at [data-rask-segments] input[type=\"hidden\"]";

    private static Page Picker(Func<Rask.Core.Component> picker, object model, Action? saved = null) =>
        Acting.Render(() => Form.Model(model).OnSubmit(_ => saved?.Invoke())[picker()]);

    [Fact]
    public void A_date_picker_with_no_type_is_typed_with_the_header_choosing_month_and_year()
    {
        var picker = Ui.DatePicker.Value(new DateOnly(2026, 11, 6)).Locale("hu-HU");

        var html = picker.ToHtml();

        Assert.Contains("data-rask-segments", html, StringComparison.Ordinal);
        Assert.Contains("<select", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ui-date-picker-button", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_picker_told_the_button_type_draws_fluxs_button_and_its_plain_header()
    {
        var picker = Ui.DatePicker.Value(new DateOnly(2026, 11, 6)).Type(Ui.DatePickerType.Button).Locale("en-US");

        var html = picker.ToHtml();

        Assert.Contains("data-ui-date-picker-button", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-segments", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<select", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_told_the_button_type_draws_one_button_and_no_typed_fields()
    {
        var picker = Ui.DatePicker.Range.Value(new UiDateRange(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6)))
            .Type(Ui.DatePickerType.Button).Locale("en-US");

        var html = picker.ToHtml();

        Assert.Contains("data-ui-date-picker-button", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-segments", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_day_picked_for_a_bound_datetime_keeps_its_time_of_day_seconds_and_kind()
    {
        var plan = new Plan { Due = new DateTime(2025, 3, 14, 8, 30, 15, DateTimeKind.Utc) };
        var page = Picker(() => Ui.DatePicker.Bind(() => plan.Due).Locale("en-US").Id("at"), plan);

        await page.On(Typed).Change("2025-03-20");
        await Acting.Act(page);

        Assert.Equal(new DateTime(2025, 3, 20, 8, 30, 15, DateTimeKind.Utc), plan.Due);
        Assert.Equal(DateTimeKind.Utc, plan.Due.Kind);
    }

    [Fact]
    public async Task A_first_day_picked_for_an_unset_datetime_is_its_midnight()
    {
        var plan = new Plan();
        var page = Picker(() => Ui.DatePicker.Bind(() => plan.Maybe).Locale("en-US").Id("at"), plan);

        await page.On(Typed).Change("2025-03-20");
        await Acting.Act(page);

        Assert.Equal(new DateTime(2025, 3, 20), plan.Maybe);
    }

    [Fact]
    public async Task A_datetime_that_may_hold_none_empties_to_null()
    {
        var plan = new Plan { Maybe = new DateTime(2025, 3, 14, 8, 30, 0) };
        var page = Picker(() => Ui.DatePicker.Bind(() => plan.Maybe).Locale("en-US").Id("at"), plan);

        await page.On(Typed).Change(string.Empty);
        await Acting.Act(page);

        Assert.Null(plan.Maybe);
    }

    [Fact]
    public async Task A_datetime_that_cannot_be_empty_says_so_under_the_picker_and_keeps_its_moment()
    {
        var plan = new Plan { Due = new DateTime(2025, 3, 14, 8, 30, 0) };
        var saves = 0;
        var page = Picker(() => Ui.DatePicker.Bind(() => plan.Due).Locale("en-US").Label("Due").Id("at"), plan, () => saves++);

        await page.On(Typed).Change(string.Empty);
        await page.On("form").Submit();

        Assert.Equal("This field is required.", page.TextOf("#at-error"));
        Assert.Equal(new DateTime(2025, 3, 14, 8, 30, 0), plan.Due);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_bound_datetimes_own_rules_say_their_message_under_the_picker()
    {
        var plan = new Plan();
        var saves = 0;
        var page = Picker(() => Ui.DatePicker.Bind(() => plan.Required).Locale("en-US").Label("Required").Id("at"), plan, () => saves++);

        await page.On("form").Submit();

        Assert.Equal("The Required field is required.", page.TextOf("#at-error"));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_day_value_object_shows_its_day_and_is_made_again_on_the_day_picked()
    {
        var invoice = new Invoice { Day = InvoiceDay.Create(new DateOnly(2025, 3, 10)) };
        using var clock = Clock.Fake(at: new DateTimeOffset(2025, 3, 14, 9, 0, 0, TimeSpan.Zero));
        var page = Picker(() => Ui.DatePicker.Bind(() => invoice.Day).Locale("en-US").Id("at"), invoice);
        var shown = page.Find("[data-ui-date-picker] > input[type=\"hidden\"]").Attribute("value");

        await page.On(Typed).Change("2025-03-12");
        await Acting.Act(page);

        Assert.Equal("2025-03-10", shown);
        Assert.Equal(InvoiceDay.Create(new DateOnly(2025, 3, 12)), invoice.Day);
    }

    [Fact]
    public async Task A_day_its_value_object_refuses_is_said_under_the_picker_and_never_written()
    {
        var invoice = new Invoice { Day = InvoiceDay.Create(new DateOnly(2025, 3, 10)) };
        using var clock = Clock.Fake(at: new DateTimeOffset(2025, 3, 14, 9, 0, 0, TimeSpan.Zero));
        var saves = 0;
        var page = Picker(() => Ui.DatePicker.Bind(() => invoice.Day).Locale("en-US").Label("Day").Id("at"), invoice, () => saves++);
        var calendar = page.Find("[data-rask-calendar]").Attribute("data-rask-calendar");

        await page.On(Typed).Change("2025-03-20");
        await page.On("form").Submit();

        Assert.Contains("\"max\":\"2025-03-14\"", calendar, StringComparison.Ordinal);
        Assert.NotEqual("", page.TextOf("#at-error"));
        Assert.Equal(InvoiceDay.Create(new DateOnly(2025, 3, 10)), invoice.Day);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task A_day_value_object_that_may_hold_none_empties_to_null()
    {
        var invoice = new Invoice { Day = InvoiceDay.Create(new DateOnly(2025, 3, 10)) };
        using var clock = Clock.Fake(at: new DateTimeOffset(2025, 3, 14, 9, 0, 0, TimeSpan.Zero));
        var page = Picker(() => Ui.DatePicker.Bind(() => invoice.Day).Locale("en-US").Id("at"), invoice);

        await page.On(Typed).Change(string.Empty);
        await Acting.Act(page);

        Assert.Null(invoice.Day);
    }

    [Fact]
    public async Task A_moment_value_object_keeps_its_time_of_day_on_the_day_picked()
    {
        var fueling = new Fueling { At = FueledAt.Create(new DateTime(2025, 3, 10, 14, 35, 20)) };
        var page = Picker(() => Ui.DatePicker.Bind(() => fueling.At).Locale("en-US").Id("at"), fueling);

        await page.On(Typed).Change("2025-03-12");
        await Acting.Act(page);

        Assert.Equal(FueledAt.Create(new DateTime(2025, 3, 12, 14, 35, 20)), fueling.At);
    }

    [Fact]
    public async Task A_first_day_picked_for_an_unset_moment_value_object_is_its_midnight()
    {
        var fueling = new Fueling();
        var page = Picker(() => Ui.DatePicker.Bind(() => fueling.At).Locale("en-US").Id("at"), fueling);

        await page.On(Typed).Change("2025-03-12");
        await Acting.Act(page);

        Assert.Equal(FueledAt.Create(new DateTime(2025, 3, 12)), fueling.At);
    }

    private sealed class Plan
    {
        public DateTime Due { get; set; }

        public DateTime? Maybe { get; set; }

        [Required]
        public DateTime? Required { get; set; }
    }

    private sealed class Invoice
    {
        public InvoiceDay? Day { get; set; }
    }

    private sealed class Fueling
    {
        public FueledAt? At { get; set; }
    }
}

/// <summary>A day no later than today, for <see cref="UiDatePickerBindTests" />.</summary>
public sealed partial record InvoiceDay(DateOnly Value) : Day(Value, NotAfterToday: true);

/// <summary>A moment, for <see cref="UiDatePickerBindTests" />.</summary>
public sealed partial record FueledAt(DateTime Value) : Moment(Value);
