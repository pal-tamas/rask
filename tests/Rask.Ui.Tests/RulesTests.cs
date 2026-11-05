namespace Rask.UiTests;

/// <summary>
///     <c>Rules</c>: the framework's sentences for a form's common rules, read from the <c>RaskStrings</c> catalog —
///     the kit's Hungarian for a Hungarian reader, the English otherwise.
/// </summary>
public class RulesTests
{
    [Fact]
    public void A_required_text_says_it_must_be_filled_then_that_it_is_too_long_in_hungarian()
    {
        var empty = "  ";
        var long_ = new string('x', 11);

        var saidOfEmpty = KitCulture.In("hu-HU", () => Rules.Text(empty, 10).Single());
        var saidOfLong = KitCulture.In("hu-HU", () => Rules.Text(long_, 10).Single());

        Assert.Equal("Kötelező kitölteni.", saidOfEmpty);
        Assert.Equal("Maximum 10 karakter hosszúnak kell lennie.", saidOfLong);
    }

    [Fact]
    public void An_english_reader_reads_the_same_rules_in_english()
    {
        var long_ = new string('x', 101);

        var saidOfEmpty = KitCulture.In("en-US", () => Rules.Text(null, 100).Single());
        var saidOfLong = KitCulture.In("en-US", () => Rules.Text(long_, 100).Single());

        Assert.Equal("This field is required.", saidOfEmpty);
        Assert.Equal("Enter at most 100 characters.", saidOfLong);
    }

    [Fact]
    public void A_text_within_its_length_says_nothing()
    {
        var fits = "Kovács János";

        var said = Rules.Text(fits, fits.Length);

        Assert.Empty(said);
    }

    [Fact]
    public void An_optional_text_may_be_empty_but_not_too_long()
    {
        var long_ = new string('x', 6);

        var saidOfEmpty = KitCulture.In("hu-HU", () => string.Concat(Rules.Optional(null, 5)));
        var saidOfLong = KitCulture.In("hu-HU", () => Rules.Optional(long_, 5).Single());

        Assert.Equal(string.Empty, saidOfEmpty);
        Assert.Equal("Maximum 5 karakter hosszúnak kell lennie.", saidOfLong);
    }

    [Fact]
    public void A_value_that_must_be_given_says_so_only_when_it_is_missing()
    {
        int? missing = null;
        int? given = 0;

        var saidOfMissing = KitCulture.In("hu-HU", () => Rules.Given(missing).Single());
        var saidOfGiven = Rules.Given(given);

        Assert.Equal("Kötelező kitölteni.", saidOfMissing);
        Assert.Empty(saidOfGiven);
    }
}
