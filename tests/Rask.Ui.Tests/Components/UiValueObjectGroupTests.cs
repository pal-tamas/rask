using Rask.Values;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Where a tank's stock count starts: a day and a quantity, both or neither — a value object of several values, as
///     a domain declares it, its rules about one member said under that member and one about the whole under it.
/// </summary>
/// <param name="Date">The day the count starts.</param>
/// <param name="Quantity">The litres it starts from.</param>
public sealed partial record TankOpening(DateOnly? Date, decimal? Quantity) : Value
{
    /// <inheritdoc />
    protected override IEnumerable<Problem> Rules()
    {
        if (Date is null && Quantity is not null)
        {
            yield return Problem.At(Date, global::Rask.Rules.Required);
        }

        if (Quantity is null && Date is not null)
        {
            yield return Problem.At(Quantity, global::Rask.Rules.Required);
        }

        if (Quantity is < 1)
        {
            yield return Problem.At(Quantity, "Enter one litre or more.");
        }

        if (Quantity > 50000)
        {
            yield return Problem.Whole("An opening is 50000 litres at most.");
        }
    }
}

/// <summary>A trip's odometer readings: a range, its end after its start.</summary>
/// <param name="Start">The reading at the start.</param>
/// <param name="End">The reading at the end.</param>
public sealed partial record TripKm(int Start, int End) : Rask.Values.Range(Start, End, Strict: true, Min: 0);

/// <summary>
///     A form's model of its own that holds a value object of several values as it is — <c>TankOpening? Start</c>, or a
///     <c>Change&lt;TankOpening?&gt;</c> — bound a member at a time: each member is typed into on its own, and the
///     property is set only to a value object its rules take whole.
/// </summary>
public sealed partial class UiValueObjectGroupTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly March = new(2026, 3, 1);

    private sealed class TankForm
    {
        public TankOpening? Start { get; set; }
    }

    private sealed class TankRow
    {
        public Change<TankOpening?> Start { get; set; }
    }

    private sealed class TripForm
    {
        public TripKm? Km { get; set; }
    }

    [Fact]
    public void A_group_shows_each_member_of_the_value_object_under_its_path()
    {
        var m = new TankForm { Start = TankOpening.Create(March, 500m) };

        var page = Render(m);

        Assert.Equal("2026-03-01", page.Find("input[name=\"Start.Date\"]").Attribute("value"));
        Assert.Equal("date", page.Find("input[name=\"Start.Date\"]").Attribute("type"));
        Assert.Equal("500", page.Find("input[name=\"Start.Quantity\"]").Attribute("value"));
    }

    [Fact]
    public async Task Its_members_typed_in_make_the_value_object()
    {
        var m = new TankForm();
        var page = Render(m);

        await page.Type("2026-03-01").Into("Day");
        var halfway = m.Start;
        await page.Type("700").Into("Litres");

        Assert.Null(halfway);
        Assert.Equal(TankOpening.Create(March, 700m), m.Start);
    }

    [Fact]
    public async Task A_member_its_rules_refuse_is_said_under_it_and_not_written()
    {
        var m = new TankForm { Start = TankOpening.Create(March, 500m) };
        var page = Render(m);

        await page.Type("0").Into("Litres");

        page.Shows("Enter one litre or more.");
        Assert.Equal("true", page.Find("input[name=\"Start.Quantity\"]").Attribute("aria-invalid"));
        Assert.Equal(TankOpening.Create(March, 500m), m.Start);
    }

    [Fact]
    public async Task What_its_rules_say_of_the_whole_is_said_under_the_property_and_stops_the_submit()
    {
        var m = new TankForm { Start = TankOpening.Create(March, 500m) };
        var saved = false;
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved = true)[
            Ui.Input.Bind(() => m.Start!.Date).Label("Day").Blur(),
            Ui.Input.Bind(() => m.Start!.Quantity).Label("Litres").Blur(),
            Ui.Error.For(() => m.Start)
        ]);

        await page.Type("60000").Into("Litres");
        await page.On("form").Submit();

        page.Shows("An opening is 50000 litres at most.");
        Assert.False(saved);
        Assert.Equal(TankOpening.Create(March, 500m), m.Start);
    }

    [Fact]
    public async Task Every_member_emptied_leaves_a_property_that_may_be_absent_empty()
    {
        var m = new TankForm { Start = TankOpening.Create(March, 500m) };
        var page = Render(m);

        await page.Type("").Into("Day");
        var halfway = m.Start;
        await page.Type("").Into("Litres");

        Assert.Equal(TankOpening.Create(March, 500m), halfway);
        Assert.Null(m.Start);
    }

    [Fact]
    public async Task A_change_left_untouched_keeps_what_it_shows()
    {
        var stored = TankOpening.Create(March, 500m);
        var row = new TankRow { Start = Change<TankOpening?>.Kept(stored) };
        var saved = false;
        var page = Page.Render(() => Form.Model(row).OnSubmit(_ => saved = true)[Members(() => row.Start.Value!.Date, () => row.Start.Value!.Quantity)]);

        await page.On("form").Submit();

        Assert.True(saved);
        Assert.Equal("500", page.Find("input[name=\"Start.Quantity\"]").Attribute("value"));
        Assert.Equal(ChangeKind.Keep, row.Start.Kind);
    }

    [Fact]
    public async Task A_change_typed_into_sets_the_new_value_and_typed_back_keeps_again()
    {
        var row = new TankRow { Start = Change<TankOpening?>.Kept(TankOpening.Create(March, 500m)) };
        var page = Page.Render(() => Form.Model(row)[Members(() => row.Start.Value!.Date, () => row.Start.Value!.Quantity)]);

        await page.Type("700").Into("Litres");
        var set = row.Start;
        await page.Type("500").Into("Litres");

        Assert.Equal(Change<TankOpening?>.Set(TankOpening.Create(March, 700m)), set);
        Assert.Equal(ChangeKind.Keep, row.Start.Kind);
    }

    [Fact]
    public async Task A_change_with_every_member_emptied_clears()
    {
        var row = new TankRow { Start = Change<TankOpening?>.Kept(TankOpening.Create(March, 500m)) };
        var page = Page.Render(() => Form.Model(row)[Members(() => row.Start.Value!.Date, () => row.Start.Value!.Quantity)]);

        await page.Type("").Into("Day");
        await page.Type("").Into("Litres");

        Assert.Equal(ChangeKind.Clear, row.Start.Kind);
    }

    [Fact]
    public async Task A_save_refused_under_a_member_is_said_under_its_control()
    {
        var m = new TankForm { Start = TankOpening.Create(March, 500m) };
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ =>
            throw new global::Rask.Cqrs.RaskValidationException([new global::Rask.Wire.FieldFailure("Counted already.", ["Start.Quantity"])]))[
            Members(() => m.Start!.Date, () => m.Start!.Quantity)
        ]);

        await page.Type("600").Into("Litres");
        await page.On("form").Submit();

        page.Shows("Counted already.");
        Assert.Equal("true", page.Find("input[name=\"Start.Quantity\"]").Attribute("aria-invalid"));
    }

    [Fact]
    public async Task A_range_held_as_it_is_draws_both_ends_and_says_a_backwards_end_under_it()
    {
        var m = new TripForm { Km = TripKm.Create(10, 20) };
        var page = Page.Render(() => Form.Model(m)[Ui.Field.Bind(() => m.Km).Blur()]);

        var start = page.Find("input[name=\"Km.Start\"]").Attribute("value");
        await page.On("input[name=\"Km.End\"]").Change("5");

        Assert.Equal("10", start);
        Assert.Equal("true", page.Find("input[name=\"Km.End\"]").Attribute("aria-invalid"));
        Assert.Equal(TripKm.Create(10, 20), m.Km);
    }

    private static Page Render(TankForm m) =>
        Page.Render(() => Form.Model(m)[Members(() => m.Start!.Date, () => m.Start!.Quantity)]);

    private static global::Rask.Core.Component[] Members(
        System.Linq.Expressions.Expression<Func<DateOnly?>> date, System.Linq.Expressions.Expression<Func<decimal?>> quantity) =>
        [Ui.Field[Ui.Input.Bind(date).Label("Day").Blur(), Ui.Error], Ui.Field[Ui.Input.Bind(quantity).Label("Litres").Blur(), Ui.Error]];
}
