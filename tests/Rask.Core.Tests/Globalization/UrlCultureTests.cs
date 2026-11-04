using System.Globalization;
using Rask.Core.Globalization;
using Rask.Core.Routing;
using Rask.Core.Tests.Routing;

namespace Rask.Core.Tests.Globalization;

// The URL always wins (#1199): a deep link opens in the language it names whatever the visitor's cookie or browser
// says, and negotiation only sends a first visit to the bare root on to its language's root.
public class UrlCultureTests
{
    private static RaskCultureOptions Options() =>
        new() { UrlPrefix = true, SupportedCultures = { "hu", "en", "de" } };

    private static CultureNegotiation Negotiated(string name, CultureSource source)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        return new CultureNegotiation(culture, culture, source);
    }

    [Theory]
    [InlineData("/en/rolunk", "en")]
    [InlineData("/de", "de")]
    [InlineData("/rolunk", "hu")]
    public void A_deep_link_opens_in_its_own_language_without_asking_the_browser(string path, string expected)
    {
        var options = Options();
        var asked = false;

        var (culture, sendTo) = UrlCulture.Arrive(
            path, UrlLanguages.From(options)!, options, () => { asked = true; return Negotiated("de", CultureSource.Cookie); });

        Assert.Equal(expected, culture.UICulture.Name);
        Assert.Null(sendTo);
        Assert.False(asked);
    }

    [Theory]
    [InlineData(CultureSource.Cookie)]
    [InlineData(CultureSource.Client)]
    [InlineData(CultureSource.Query)]
    public void A_first_visit_to_the_bare_root_is_sent_on_to_its_negotiated_languages_root(CultureSource source)
    {
        var options = Options();

        var (culture, sendTo) = UrlCulture.Arrive("/", UrlLanguages.From(options)!, options, () => Negotiated("en", source));

        Assert.Equal("en", culture.UICulture.Name);
        Assert.Equal("/en", sendTo);
    }

    [Fact]
    public void A_visit_to_the_bare_root_that_negotiates_the_default_language_stays()
    {
        var options = Options();

        var (culture, sendTo) = UrlCulture.Arrive("/", UrlLanguages.From(options)!, options, () => Negotiated("hu", CultureSource.Cookie));

        Assert.Equal("hu", culture.UICulture.Name);
        Assert.Null(sendTo);
    }

    [Fact]
    public void A_visit_to_the_bare_root_with_nothing_to_go_on_stays_in_the_default()
    {
        var options = Options();

        var (culture, sendTo) = UrlCulture.Arrive("/", UrlLanguages.From(options)!, options, () => Negotiated("en", CultureSource.Default));

        Assert.Equal("hu", culture.UICulture.Name);
        Assert.Null(sendTo);
    }

    [Fact]
    public async Task Moving_to_an_address_in_another_language_switches_the_culture_and_remembers_it()
    {
        var options = Options();
        var persistence = new RecordingPersistence();
        var culture = new SessionCulture(options, persistence);
        var route = new RouteState { Languages = UrlLanguagesTests.Languages("hu", "en", "de") };
        UrlCulture.Follow(route, culture);

        route.Path = "/en/rolunk";
        await persistence.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("en", culture.UICulture.Name);
        Assert.Equal("en", persistence.Last);
    }

    [Fact]
    public void Moving_within_one_language_leaves_the_culture_alone()
    {
        var options = Options();
        var persistence = new RecordingPersistence();
        var culture = new SessionCulture(options, persistence);
        var route = new RouteState { Languages = UrlLanguagesTests.Languages("hu", "en", "de") };
        UrlCulture.Follow(route, culture);

        route.Path = "/rolunk";
        route.Path = "/kapcsolat";

        Assert.Equal("hu", culture.UICulture.Name);
        Assert.Null(persistence.Last);
    }

    private sealed class RecordingPersistence : IRaskCulturePersistence
    {
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? Last { get; private set; }

        public Task Save(string culture, string uiCulture, CancellationToken cancellationToken = default)
        {
            Last = culture;
            Saved.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
