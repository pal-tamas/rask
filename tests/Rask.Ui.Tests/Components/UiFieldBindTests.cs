using System.ComponentModel.DataAnnotations;
using Rask.Core.Forms;
using Rask.Testing;
using Rask.Wire;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Field.Bind(() =&gt; m.X)</c> draws the field its member calls for — the control its shape or its type asks for,
///     labelled, described and placeheld by its <c>[Display]</c> — while <c>Ui.Field[…]</c> stays the hand-made field.
/// </summary>
public partial class UiFieldBindTests : global::Rask.Core.RaskMarkup
{
    private static readonly FieldOption[] Levels = [new(Level.Low, "Kicsi"), new(Level.High, "Nagy")];

    static UiFieldBindTests() =>
        RaskValidation.RegisterFieldShapes(typeof(Leg), static name => name switch
        {
            nameof(Leg.Tank) => new FieldShape(FieldKind.Number, Min: 1, Max: 500, Scale: 2),
            nameof(Leg.Axles) => new FieldShape(FieldKind.Whole, Min: 2, Max: 9),
            nameof(Leg.Day) => new FieldShape(FieldKind.Day, NotAfterToday: true),
            nameof(Leg.Level) or nameof(Leg.Fixed) => new FieldShape(FieldKind.Choice, Options: Levels),
            nameof(Leg.Vehicle) => new FieldShape(FieldKind.Id),
            nameof(Leg.Km) => new FieldShape(FieldKind.Range, Min: 0, Start: "Start", End: "End", Ends: FieldKind.Whole),
            _ => null,
        });

    [Fact]
    public void A_text_field_takes_its_label_help_text_and_placeholder_from_the_members_display()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Name));

        Assert.Equal("Driver", page.TextOf("label[for=\"f-name\"]").Replace("Required", "", StringComparison.Ordinal).Trim());
        Assert.Equal("Who drives", page.TextOf("#f-name-description"));
        Assert.Equal("Full name", page.Find("input#f-name").Attribute("placeholder"));
    }

    [Fact]
    public void A_field_that_must_be_filled_carries_the_required_badge()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Name));

        Assert.Contains("Required", page.TextOf("label[for=\"f-name\"]"), StringComparison.Ordinal);
        Assert.True(page.Exists("input#f-name[aria-required=\"true\"]"));
    }

    [Fact]
    public void The_steps_win_over_the_members_display()
    {
        var leg = new Leg();

        var page = Page.Render(() =>
            Ui.Field.Bind(() => leg.Name).Label("Sofőr").Description("Aki vezet").Placeholder("Név").Badge(""));

        Assert.Equal("Sofőr", page.TextOf("label[for=\"f-name\"]"));
        Assert.Equal("Aki vezet", page.TextOf("#f-name-description"));
        Assert.Equal("Név", page.Find("input#f-name").Attribute("placeholder"));
    }

    [Fact]
    public void An_email_member_is_an_email_input()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Email));

        Assert.Equal("email", page.Find("input#f-email").Attribute("type"));
    }

    [Fact]
    public void A_number_takes_its_bounds_and_a_step_of_its_scale()
    {
        var leg = new Leg();

        var page = Page.Render(() => Div[Ui.Field.Bind(() => leg.Tank), Ui.Field.Bind(() => leg.Axles)]);

        var tank = page.Find("input#f-tank");
        Assert.Equal(("number", "1", "500", "0.01"), (tank.Attribute("type"), tank.Attribute("min"), tank.Attribute("max"), tank.Attribute("step")));
        var axles = page.Find("input#f-axles");
        Assert.Equal(("2", "9", "1"), (axles.Attribute("min"), axles.Attribute("max"), axles.Attribute("step")));
    }

    [Fact]
    public void A_day_that_may_not_be_after_today_is_a_date_picker_ending_today()
    {
        var leg = new Leg();
        using var clock = Clock.Fake(at: new DateTimeOffset(2025, 3, 14, 9, 0, 0, TimeSpan.Zero));

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Day));

        Assert.Contains("\"max\":\"2025-03-14\"", page.Find("[data-rask-calendar]").Attribute("data-rask-calendar"), StringComparison.Ordinal);
    }

    [Fact]
    public void An_enum_is_a_select_of_its_options_in_order_with_an_empty_one_for_a_member_that_may_hold_none()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Level));

        var options = page.FindAll("select#f-level option");
        Assert.Equal(["", "Kicsi", "Nagy"], options.Select(o => o.TextContent.Trim()));
        Assert.Equal(["", "Low", "High"], options.Select(o => o.Attribute("value")));
    }

    [Fact]
    public void An_id_is_a_select_over_the_choices_given()
    {
        var leg = new Leg();
        var truck = Guid.NewGuid();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Vehicle).Choices([new Ui.Choice<Guid>(truck, "ABC-123")]));

        Assert.Contains("ABC-123", page.Render(), StringComparison.Ordinal);
        Assert.True(page.Exists("label[for=\"f-vehicle\"]"));
    }

    [Fact]
    public void An_id_given_no_choices_says_to_give_them()
    {
        var leg = new Leg();

        var error = Assert.ThrowsAny<Exception>(() => Page.Render(() => Ui.Field.Bind(() => leg.Vehicle)));

        Assert.Contains(".Choices(", Innermost(error).Message, StringComparison.Ordinal);
        Assert.Contains("Vehicle", Innermost(error).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_is_two_controls_under_one_label()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Km));

        Assert.Equal("Kilometres", page.TextOf("label[for=\"f-km-start\"]"));
        Assert.Equal(("0", "1"), (page.Find("input#f-km-start").Attribute("min"), page.Find("input#f-km-start").Attribute("step")));
        Assert.True(page.Exists("input#f-km-end"));
        Assert.Single(page.FindAll("label"));
    }

    [Fact]
    public async Task Each_end_of_a_range_says_its_own_message()
    {
        var leg = new Leg { Name = "Kovács" };
        var page = Page.Render(() => Form.Model(leg).OnSubmit(_ => Task.FromException(new Refused("Backwards.", "Km.End")))[
            Ui.Field.Bind(() => leg.Km),
            Ui.Button.Submit["Save"]
        ]);

        await page.On("form").Submit();

        Assert.Equal("Backwards.", page.TextOf("#f-km-end-error"));
        Assert.Equal("", page.TextOf("#f-km-start-error"));
    }

    [Fact]
    public void A_bool_is_a_checkbox()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Loaded));

        Assert.True(page.Exists("[data-ui-checkbox]"));
    }

    [Fact]
    public void Multiline_text_is_a_textarea()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Bind(() => leg.Note).Multiline());

        Assert.True(page.Exists("textarea#f-note"));
        Assert.False(page.Exists("input#f-note"));
    }

    [Fact]
    public void A_member_not_to_be_edited_is_a_read_only_input_and_a_disabled_select()
    {
        var leg = new Leg();

        var page = Page.Render(() => Div[Ui.Field.Bind(() => leg.Plate), Ui.Field.Bind(() => leg.Fixed)]);

        Assert.True(page.Exists("input#f-plate[readonly]"));
        Assert.False(page.Exists("input#f-plate[disabled]"));
        Assert.True(page.Exists("select#f-fixed[disabled]"));
    }

    [Fact]
    public void The_container_still_draws_the_parts_it_is_given()
    {
        var leg = new Leg();

        var page = Page.Render(() => Ui.Field.Class("mt-4")[Ui.Label["Notes"], Ui.Input.Bind(() => leg.Note), Ui.Error]);

        Assert.True(page.Exists("[data-ui-field].mt-4"));
        Assert.Equal("Notes", page.TextOf("label[for=\"f-note\"]"));
        Assert.Single(page.FindAll("[data-ui-field]"));
    }

    private static Exception Innermost(Exception error) => error.InnerException is { } inner ? Innermost(inner) : error;

    private enum Level
    {
        Low,
        High,
    }

    private sealed class Leg
    {
        [Required]
        [Display(Name = "Driver", Description = "Who drives", Prompt = "Full name")]
        public string Name { get; set; } = "";

        [EmailAddress]
        public string? Email { get; set; }

        public decimal? Tank { get; set; }

        public int? Axles { get; set; }

        public DateOnly? Day { get; set; }

        public Level? Level { get; set; }

        [Display(Name = "Vehicle")]
        public Guid? Vehicle { get; set; }

        [Display(Name = "Kilometres")]
        public Km Km { get; set; } = new();

        public bool? Loaded { get; set; }

        public string? Note { get; set; }

        [Editable(false)]
        public string? Plate { get; set; }

        [Editable(false)]
        public Level? Fixed { get; set; }
    }

    private sealed class Km
    {
        public int? Start { get; set; }

        public int? End { get; set; }
    }

    private sealed class Refused(string message, string field) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures { get; } = [new FieldFailure(message, [field])];
    }
}
