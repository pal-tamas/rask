using Rask.Values;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A tank's capacity in litres: a value object of one whole number, as a domain declares it.</summary>
/// <param name="Value">The litres.</param>
public sealed partial record TankCapacity(int Value) : Integer(Value, Min: 100, Max: 50000);

/// <summary>A tank's name: a value object of text.</summary>
/// <param name="Value">The name.</param>
public sealed partial record TankName(string Value) : Text(Value, MaxLength: 20);

/// <summary>The day a tank was filled: a value object of a day, none after today.</summary>
/// <param name="Value">The day.</param>
public sealed partial record FilledOn(DateOnly Value) : Day(Value, NotAfterToday: true);

/// <summary>
///     A form's model of its own that holds value objects as they are — no primitive beside them, no <c>.Validate(…)</c>
///     on the field: the control shows and is typed into the value each holds, and the value object's rules say what is
///     wrong, as a field of <c>Form.For</c> does.
/// </summary>
public sealed partial class UiValueObjectFieldTests : global::Rask.Core.RaskMarkup
{
    private sealed class TankForm
    {
        public TankCapacity? Size { get; set; }

        public TankName Name { get; set; } = TankName.Create("Main");

        public FilledOn? Filled { get; set; }
    }

    private sealed class NewTankForm
    {
        public TankName Name { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required]
        public TankCapacity? Size { get; set; }
    }

    [Fact]
    public void A_bound_value_object_shows_its_value_with_the_shape_its_rules_declare()
    {
        var m = new TankForm { Size = TankCapacity.Create(2000) };

        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Size).Label("Size"), Ui.Input.Bind(() => m.Name).Label("Name")]);

        var size = page.Find("input[name=\"Size\"]");
        Assert.Equal("number", size.Attribute("type"));
        Assert.Equal("2000", size.Attribute("value"));
        Assert.Equal("100", size.Attribute("min"));
        Assert.Equal("50000", size.Attribute("max"));
        Assert.Equal("20", page.Find("input[name=\"Name\"]").Attribute("maxlength"));
        Assert.Equal("Main", page.Find("input[name=\"Name\"]").Attribute("value"));
    }

    [Fact]
    public async Task A_value_its_rules_refuse_is_said_under_the_field_and_not_written()
    {
        var m = new TankForm { Size = TankCapacity.Create(2000) };
        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Size).Label("Size").Blur()]);

        await page.Type("50").Into("Size");

        page.Shows("Enter 100 or more.");
        Assert.Equal(TankCapacity.Create(2000), m.Size);
    }

    [Fact]
    public async Task A_value_its_rules_accept_is_written_as_the_value_object()
    {
        var m = new TankForm();
        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Size).Label("Size").Blur()]);

        await page.Type("3000").Into("Size");

        Assert.Equal(TankCapacity.Create(3000), m.Size);
        page.DoesNotShow("Enter 100 or more.");
    }

    [Fact]
    public async Task An_emptied_value_object_that_may_be_absent_is_null()
    {
        var m = new TankForm { Size = TankCapacity.Create(2000) };
        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Size).Label("Size").Blur()]);

        await page.Type("").Into("Size");

        Assert.Null(m.Size);
    }

    [Fact]
    public async Task An_emptied_value_object_that_must_be_given_is_required_and_kept()
    {
        var m = new TankForm();
        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Name).Label("Name").Blur()]);

        var required = page.Find("input[name=\"Name\"]").Attribute("aria-required");
        await page.Type(" ").Into("Name");

        Assert.Equal("true", required);
        page.Shows("This field is required.");
        Assert.Equal(TankName.Create("Main"), m.Name);
    }

    [Fact]
    public void A_bound_field_over_a_value_object_draws_the_control_of_its_kind()
    {
        var m = new TankForm();

        var page = Page.Render(() => Form.Model(m)[Ui.Field.Bind(() => m.Size)]);

        var size = page.Find("input[name=\"Size\"]");
        Assert.Equal("number", size.Attribute("type"));
        Assert.Equal("100", size.Attribute("min"));
        Assert.Equal("1", size.Attribute("step"));
    }

    [Fact]
    public void A_bound_field_over_a_day_held_as_a_value_object_is_a_date_input()
    {
        var m = new TankForm { Filled = FilledOn.Create(new DateOnly(2026, 3, 1)) };

        var page = Page.Render(() => Form.Model(m)[Ui.Field.Bind(() => m.Filled)]);

        var filled = page.Find("input[name=\"Filled\"]");
        Assert.Equal("date", filled.Attribute("type"));
        Assert.Equal("2026-03-01", filled.Attribute("value"));
    }

    [Fact]
    public async Task A_submit_says_what_a_value_object_never_given_and_a_required_one_lack()
    {
        var m = new NewTankForm();
        var saved = false;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved = true)[
            Ui.Field.Bind(() => m.Name),
            Ui.Field.Bind(() => m.Size)
        ]);

        await page.On("form").Submit();

        Assert.False(saved);
        Assert.Equal("true", page.Find("input[name=\"Name\"]").Attribute("aria-invalid"));
        Assert.Equal("true", page.Find("input[name=\"Size\"]").Attribute("aria-invalid"));
    }
}
