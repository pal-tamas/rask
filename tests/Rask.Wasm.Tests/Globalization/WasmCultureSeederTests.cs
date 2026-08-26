using Rask.Core.Globalization;

namespace Rask.Wasm.Tests.Globalization;

// The browser half of negotiation: what the app decides from the signals a page can offer, before its
// first render. Same order as the server's, because a visitor should not get a different language from
// the same app depending on how it is hosted.
public class WasmCultureSeederTests
{
    private static RaskCultureOptions Options(params string[] supported)
    {
        var options = new RaskCultureOptions();
        foreach (var name in supported)
        {
            options.SupportedCultures.Add(name);
        }

        return options;
    }

    [Fact]
    public void The_url_beats_the_remembered_choice_which_beats_the_browsers_list()
    {
        var options = Options("en", "hu", "de");

        Assert.Equal("hu", Negotiate("""{"query":"hu","cookie":"c=de|uic=de","languages":["en"]}""", options));
        Assert.Equal("de", Negotiate("""{"query":null,"cookie":"c=de|uic=de","languages":["en"]}""", options));
        Assert.Equal("en", Negotiate("""{"languages":["en"]}""", options));
    }

    [Fact]
    public void The_browsers_list_is_honoured_in_order()
    {
        var options = Options("en", "hu", "de");
        Assert.Equal("de", Negotiate("""{"languages":["ja","de","hu"]}""", options));
    }

    [Fact]
    public void An_unsupported_language_falls_back_to_the_apps_default()
    {
        var options = Options("en", "hu");
        Assert.Equal("en", Negotiate("""{"query":"ja-JP","languages":["ja-JP"]}""", options));
    }

    [Fact]
    public void A_region_is_served_by_the_language_the_app_ships()
    {
        var options = Options("en", "hu");
        Assert.Equal("hu", Negotiate("""{"languages":["hu-HU"]}""", options));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"query":null,"cookie":null,"languages":[]}""")]
    [InlineData("""{"languages":null}""")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"languages":[1,2,3]}""")]
    public void A_page_that_offers_nothing_usable_still_gets_the_default(string signals)
    {
        // Every one of these is a real browser state — cookies disabled, a privacy mode that hides
        // navigator.languages, an old bundle answering a shape this build does not expect. None of them
        // may take the boot down over a language preference.
        var options = Options("en", "hu");
        Assert.Equal("en", Negotiate(signals, options));
    }

    [Fact]
    public void The_cookie_is_read_in_the_form_the_server_writes_it()
    {
        // The server writes ASP.NET's own c=..|uic=.. payload, and rask.wasm.js URL-decodes the cookie
        // before handing it over — so what arrives here is the decoded pair. A visitor who picked a
        // language on the server half must keep it when the same app runs in the browser.
        var options = Options("en", "hu");
        Assert.Equal("hu", Negotiate("""{"cookie":"c=hu|uic=hu"}""", options));
        Assert.Equal("hu", Negotiate("""{"cookie":"hu"}""", options));
    }

    [Fact]
    public void An_app_with_no_configured_languages_negotiates_nothing()
    {
        var result = WasmCultureSeeder.Negotiate(
            """{"query":"hu","languages":["hu"]}""", new RaskCultureOptions());

        Assert.Equal(System.Globalization.CultureInfo.InvariantCulture, result.Culture);
    }

    private static string Negotiate(string signals, RaskCultureOptions options) =>
        WasmCultureSeeder.Negotiate(signals, options).Culture.Name;
}
