namespace Rask.Cli.Tests;

public sealed class SuggestTests
{
    private static readonly string[] Commands = ["new", "dev", "generate", "db", "deploy", "info", "completion"];

    [Theory]
    [InlineData("genrate", "generate")]   // transposition
    [InlineData("generat", "generate")]   // dropped letter
    [InlineData("generatee", "generate")] // doubled letter
    [InlineData("Deploy", "deploy")]      // case
    [InlineData("dpeloy", "deploy")]
    public void Finds_the_intended_word(string typed, string expected) =>
        Assert.Equal(expected, Suggest.Closest(typed, Commands));

    [Theory]
    [InlineData("kubernetes")]
    [InlineData("publish")]
    [InlineData("")]
    [InlineData(null)]
    public void Offers_nothing_when_nothing_is_close(string? typed) =>
        Assert.Null(Suggest.Closest(typed, Commands));

    [Fact]
    public void A_short_word_gets_a_smaller_budget()
    {
        // "db" and "dev" are both two edits from "dxy"; correcting a three-letter word that far is a
        // guess, not a suggestion.
        Assert.Null(Suggest.Closest("dxy", Commands));
        Assert.Equal("db", Suggest.Closest("dn", Commands));
    }

    [Fact]
    public void An_unambiguous_prefix_counts_as_a_match() =>
        Assert.Equal("generate", Suggest.Closest("gene", Commands));

    [Fact]
    public void An_unambiguous_prefix_beats_a_nearer_edit()
    {
        // "dep" is a single substitution from "dev", but only "deploy" starts with it — an abbreviation
        // says more about the intent than the edit count does.
        Assert.Equal("deploy", Suggest.Closest("dep", ["dev", "deploy"]));
    }

    [Fact]
    public void An_ambiguous_prefix_is_not_a_match()
    {
        // Both start with "sna", so the prefix identifies neither, and neither is within an edit budget.
        Assert.Null(Suggest.Closest("sna", ["snapshots", "snake"]));
    }

    [Fact]
    public void Prefers_the_closest_of_several_candidates() =>
        Assert.Equal("page", Suggest.Closest("pag", ["page", "cache", "job"]));

    [Fact]
    public void An_exact_match_returns_itself() =>
        Assert.Equal("deploy", Suggest.Closest("deploy", Commands));

    [Fact]
    public void No_candidates_is_not_an_error() =>
        Assert.Null(Suggest.Closest("anything", []));
}
