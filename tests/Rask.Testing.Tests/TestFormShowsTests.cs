using System.ComponentModel.DataAnnotations;

namespace Rask.Testing.Tests;

// Test.FormShows puts the model in a real form, so what it proves is what the page tells the reader.
public sealed class TestFormShowsTests
{
    private const string TooLittle = "At least one litre.";
    private const string NameNeeded = "A supply needs a name.";
    private const string EndBeforeStart = "The end comes after the start.";

    public sealed class SupplyModel
    {
        [Required(ErrorMessage = NameNeeded)]
        public string? Name { get; set; }

        public List<PartModel> Parts { get; set; } = [];

        public KmModel Km { get; set; } = new();
    }

    public sealed class PartModel
    {
        [Range(1, 1000, ErrorMessage = TooLittle)]
        public decimal Quantity { get; set; } = 1;
    }

    public sealed class KmModel : IValidatableObject
    {
        public int Start { get; set; }

        public int End { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (End <= Start)
            {
                yield return new ValidationResult(EndBeforeStart, [nameof(End)]);
            }
        }
    }

    [Fact]
    public async Task A_field_shows_its_attributes_message_for_a_value_it_refuses()
    {
        var shows = Test.FormShows<SupplyModel>(m => m.Name, "", NameNeeded);

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_row_the_list_does_not_have_yet_is_added_to_reach_its_field()
    {
        var shows = Test.FormShows<SupplyModel>(m => m.Parts[1].Quantity, 0, TooLittle);

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_rule_over_several_fields_is_shown_once_the_others_are_given()
    {
        var shows = Test.FormShows<SupplyModel>(m => m.Km.End, value: 5, given: m => m.Km.Start = 10, EndBeforeStart);

        await shows;

        Assert.True(shows.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_model_made_by_hand_is_the_form_s_start()
    {
        var model = new SupplyModel { Km = new KmModel { Start = 10 } };

        await Test.FormShows(model, m => m.Km.End, 3, EndBeforeStart);

        Assert.Equal(3, model.Km.End);
    }

    [Fact]
    public async Task A_value_the_form_accepts_fails_the_test_and_says_what_was_shown()
    {
        var shows = () => Test.FormShows<SupplyModel>(m => m.Parts[0].Quantity, 5m, TooLittle);

        var failed = await Assert.ThrowsAsync<FormAssertionException>(shows);

        Assert.Contains("under Parts[0].Quantity holding 5", failed.Message, StringComparison.Ordinal);
        Assert.Contains("it showed nothing", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_message_than_the_one_expected_is_named()
    {
        var shows = () => Test.FormShows<SupplyModel>(m => m.Name, "", "Something else.");

        var failed = await Assert.ThrowsAsync<FormAssertionException>(shows);

        Assert.Contains($"it showed \"{NameNeeded}\"", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lambda_that_is_no_field_of_the_model_is_refused_with_the_shape_it_takes()
    {
        var shows = () => Test.FormShows<SupplyModel>(m => m.Parts.Count, 1, TooLittle);

        var refused = await Assert.ThrowsAsync<ArgumentException>(shows);

        Assert.Contains("m => m.Parts[0].Quantity", refused.Message, StringComparison.Ordinal);
    }
}
