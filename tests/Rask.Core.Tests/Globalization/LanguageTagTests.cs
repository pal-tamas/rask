using Rask.Core.Globalization;

namespace Rask.Core.Tests.Globalization;

// Matching a requested language against the app's list by the TAG, never through CultureInfo. A runtime with no
// culture data gives every culture the invariant one as its parent, so the old walk over CultureInfo.Parent never
// reached a supported "hu" from "hu-HU" there, and a Hungarian visitor read the default language.
public class LanguageTagTests
{
    [Fact]
    public void A_region_is_served_by_the_language_the_app_ships()
    {
        string[] supported = ["en", "hu"];

        var match = LanguageTag.Match("hu-HU", supported);

        Assert.Equal("hu", match);
    }

    [Fact]
    public void A_language_is_served_by_the_region_the_app_ships()
    {
        string[] supported = ["en", "hu-HU"];

        var match = LanguageTag.Match("hu", supported);

        Assert.Equal("hu-HU", match);
    }

    [Fact]
    public void An_exact_region_wins_over_its_language()
    {
        string[] supported = ["pt", "pt-BR"];

        var match = LanguageTag.Match("pt-BR", supported);

        Assert.Equal("pt-BR", match);
    }

    [Fact]
    public void Case_and_an_underscore_are_not_differences()
    {
        string[] supported = ["en", "de-AT"];

        var match = LanguageTag.Match("DE_at", supported);

        Assert.Equal("de-AT", match);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_language_the_app_does_not_ship_matches_nothing(string? requested)
    {
        string[] supported = ["en", "hu"];

        var match = LanguageTag.Match(requested, supported);

        Assert.Null(match);
    }

    [Fact]
    public void An_unknown_but_well_formed_tag_still_matches_by_its_subtags()
    {
        // No runtime knows "xx", with or without ICU, so this can only pass if nothing asks one.
        string[] supported = ["en", "xx"];

        var match = LanguageTag.Match("xx-YY", supported);

        Assert.Equal("xx", match);
    }
}
