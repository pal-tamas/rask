namespace Rask.Testing.Database.Tests;

/// <summary>A tank's size: a whole number of litres in a band — a value object of one value.</summary>
public sealed partial record TankCapacity(int Value) : Integer(Value, Min: 100, Max: 50000);

/// <summary>Where a tank's count starts: a day and a quantity — a value object of several values, with a rule of its own.</summary>
public sealed partial record TankOpening(DateOnly? Date, decimal? Quantity) : Value
{
    public const string AtLeastALitre = "Enter one litre or more.";

    protected override IEnumerable<Problem> Rules()
    {
        if (Quantity is < 1)
        {
            yield return Problem.At(Quantity, AtLeastALitre);
        }
    }
}

/// <summary>
///     Test.FormShows over a hand form model that holds value objects as they are: the value given is what the reader
///     types, made into the value object through the binding a control's goes through, and its refusal is said where
///     the page says it.
/// </summary>
public sealed class FormShowsValueObjectTests
{
    private const string TooSmall = "Enter 100 or more.";

    public sealed class TankModel
    {
        public TankCapacity? Size { get; set; }

        public TankCapacity Required { get; set; } = TankCapacity.Create(1000);

        public TankOpening? Start { get; set; }
    }

    public sealed class TankRow
    {
        public Change<TankOpening?> Start { get; set; }
    }

    [Fact]
    public async Task A_value_object_of_one_value_is_typed_in_as_the_value_it_holds()
    {
        var shows = Test.FormShows<TankModel>(m => m.Size, 50, TooSmall);

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_value_object_its_rules_refuse_leaves_the_model_as_it_was()
    {
        var model = new TankModel { Size = TankCapacity.Create(1000) };

        await Test.FormShows(model, m => m.Size, 50, TooSmall);

        Assert.Equal(TankCapacity.Create(1000), model.Size);
    }

    [Fact]
    public async Task A_value_object_its_rules_take_is_written_and_a_message_it_does_not_show_fails()
    {
        var model = new TankModel();

        var error = await Assert.ThrowsAsync<FormAssertionException>(() => Test.FormShows(model, m => m.Size, 500, TooSmall));

        Assert.Equal(TankCapacity.Create(500), model.Size);
        Assert.Contains("Size holding 500", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_object_that_may_not_be_empty_says_so_when_emptied()
    {
        var shows = Test.FormShows<TankModel>(m => m.Required, null, global::Rask.Rules.Required);

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_member_of_a_value_object_of_several_values_is_typed_in_on_its_draft()
    {
        var shows = Test.FormShows<TankModel>(m => m.Start!.Quantity, 0m, TankOpening.AtLeastALitre);

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_member_of_a_change_of_a_value_object_is_typed_in_on_its_draft()
    {
        var shows = Test.FormShows<TankRow>(m => m.Start.Value!.Quantity, 0m, TankOpening.AtLeastALitre);

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }
}
