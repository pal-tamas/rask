using Rask.Core.Globalization;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

// The shape of a URL that carries its language (UseCulture(c => c.UrlPrefix = true)): the default language at the
// root, every other one as the path's first segment, and the same slug in each.
public class UrlLanguagesTests
{
    internal static UrlLanguages Languages(params string[] supported)
    {
        var options = new RaskCultureOptions { UrlPrefix = true };
        foreach (var name in supported)
        {
            options.SupportedCultures.Add(name);
        }

        return UrlLanguages.From(options)!;
    }

    [Fact]
    public void An_app_that_keeps_languages_out_of_its_urls_has_none()
    {
        var options = new RaskCultureOptions { SupportedCultures = { "hu", "en" } };

        var languages = UrlLanguages.From(options);

        Assert.Null(languages);
    }

    [Fact]
    public void The_first_language_is_the_roots_and_the_rest_follow_in_order()
    {
        var languages = Languages("hu", "en", "de");

        Assert.Equal("hu", languages.Default);
        Assert.Equal(new[] { "hu", "en", "de" }, languages.All);
    }

    [Fact]
    public void A_configured_default_culture_is_the_roots_language()
    {
        var options = new RaskCultureOptions { UrlPrefix = true, DefaultCulture = "de", SupportedCultures = { "en", "de" } };

        var languages = UrlLanguages.From(options)!;

        Assert.Equal("de", languages.Default);
        Assert.Equal(new[] { "de", "en" }, languages.All);
    }

    [Theory]
    [InlineData("/", "hu", "/")]
    [InlineData("/rolunk", "hu", "/rolunk")]
    [InlineData("/en", "en", "/")]
    [InlineData("/en/", "en", "/")]
    [InlineData("/en/rolunk", "en", "/rolunk")]
    [InlineData("/EN/rolunk/", "en", "/rolunk/")]
    [InlineData("/de/a/b", "de", "/a/b")]
    public void A_path_splits_into_its_language_and_the_route_it_is_of(string path, string language, string routePath)
    {
        var languages = Languages("hu", "en", "de");

        var split = languages.Split(path);

        Assert.Equal((language, routePath), split);
    }

    [Theory]
    [InlineData("/hu/rolunk")]
    [InlineData("/english")]
    [InlineData("/enx/a")]
    public void Only_another_languages_whole_first_segment_is_a_prefix(string path)
    {
        var languages = Languages("hu", "en");

        var split = languages.Split(path);

        Assert.Equal(("hu", path), split);
    }

    [Theory]
    [InlineData("hu", "/rolunk", "/rolunk")]
    [InlineData("en", "/rolunk", "/en/rolunk")]
    [InlineData("en", "/", "/en")]
    [InlineData("en-GB", "/rolunk", "/en/rolunk")]
    [InlineData("ja", "/rolunk", "/rolunk")]
    [InlineData(null, "/rolunk", "/rolunk")]
    public void A_route_is_joined_to_a_language_with_the_default_at_the_root(string? language, string routePath, string expected)
    {
        var languages = Languages("hu", "en");

        var address = languages.Join(language, routePath);

        Assert.Equal(expected, address);
    }
}
