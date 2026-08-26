using System.Globalization;
using Rask.Core.Globalization;

namespace Rask.Core.Tests.Globalization;

// Negotiation is pure and host-free, so it can be asserted without a server, a browser or a socket.
public class CultureNegotiationTests
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
    public void Query_beats_cookie_beats_client_beats_default()
    {
        var options = Options("en", "hu", "de");

        Assert.Equal("hu", Negotiate("hu", "c=de|uic=de", ["en"]).Culture.Name);
        Assert.Equal("de", Negotiate(null, "c=de|uic=de", ["en"]).Culture.Name);
        Assert.Equal("en", Negotiate(null, null, ["en"]).Culture.Name);
        Assert.Equal("en", Negotiate(null, null, null).Culture.Name);

        CultureNegotiation Negotiate(string? q, string? c, string[]? client) =>
            RaskCultureNegotiator.Negotiate(q, c, client, options);
    }

    [Fact]
    public void The_source_is_reported_so_a_host_knows_whether_to_remember_the_choice()
    {
        var options = Options("en", "hu");

        Assert.Equal(CultureSource.Query, RaskCultureNegotiator.Negotiate("hu", null, null, options).Source);
        Assert.Equal(CultureSource.Cookie, RaskCultureNegotiator.Negotiate(null, "c=hu|uic=hu", null, options).Source);
        Assert.Equal(CultureSource.Client, RaskCultureNegotiator.Negotiate(null, null, ["hu"], options).Source);
        Assert.Equal(CultureSource.Default, RaskCultureNegotiator.Negotiate(null, null, ["ja"], options).Source);
    }

    [Fact]
    public void A_region_falls_back_to_the_language_the_app_does_ship()
    {
        // hu-HU is not configured, hu is: the visitor gets Hungarian rather than the default.
        var options = Options("en", "hu");
        Assert.Equal("hu", RaskCultureNegotiator.Negotiate("hu-HU", null, null, options).Culture.Name);
    }

    [Fact]
    public void A_language_is_served_by_a_region_the_app_does_ship()
    {
        // The mirror case: the visitor asks for "hu", the app only lists "hu-HU". Serving it beats
        // falling through to English — they asked for a language the app has.
        var options = Options("en-US", "hu-HU");
        Assert.Equal("hu-HU", RaskCultureNegotiator.Negotiate("hu", null, null, options).Culture.Name);
    }

    [Fact]
    public void An_unsupported_language_is_ignored_rather_than_honoured()
    {
        var options = Options("en", "hu");
        var result = RaskCultureNegotiator.Negotiate("ja-JP", null, ["ja-JP"], options);

        Assert.Equal("en", result.Culture.Name);
        Assert.Equal(CultureSource.Default, result.Source);
    }

    [Fact]
    public void The_client_list_is_honoured_in_order_of_preference()
    {
        var options = Options("en", "hu", "de");
        Assert.Equal("de", RaskCultureNegotiator.Negotiate(null, null, ["ja", "de", "hu"], options).Culture.Name);
    }

    [Fact]
    public void The_first_supported_culture_is_the_default_unless_one_is_named()
    {
        Assert.Equal("hu", RaskCultureNegotiator.Negotiate(null, null, null, Options("hu", "en")).Culture.Name);

        var explicitDefault = Options("hu", "en");
        explicitDefault.DefaultCulture = "en";
        Assert.Equal("en", RaskCultureNegotiator.Negotiate(null, null, null, explicitDefault).Culture.Name);
    }

    [Fact]
    public void No_configured_cultures_means_culture_support_is_off()
    {
        var result = RaskCultureNegotiator.Negotiate("hu", "c=hu|uic=hu", ["hu"], new RaskCultureOptions());

        Assert.Equal(CultureInfo.InvariantCulture, result.Culture);
        Assert.Equal(CultureSource.Default, result.Source);
    }

    [Fact]
    public void Each_signal_can_be_turned_off_independently()
    {
        var noQuery = Options("en", "hu");
        noQuery.UseQueryString = false;
        Assert.Equal("en", RaskCultureNegotiator.Negotiate("hu", null, null, noQuery).Culture.Name);

        var noCookie = Options("en", "hu");
        noCookie.UseCookie = false;
        Assert.Equal("en", RaskCultureNegotiator.Negotiate(null, "c=hu|uic=hu", null, noCookie).Culture.Name);

        var noClient = Options("en", "hu");
        noClient.UseClientPreference = false;
        Assert.Equal("en", RaskCultureNegotiator.Negotiate(null, null, ["hu"], noClient).Culture.Name);
    }

    [Fact]
    public void An_explicit_selection_is_honoured_even_when_the_query_signal_is_off()
    {
        // UseQueryString governs how a choice TRAVELS, not whether it counts. Routing a switcher through
        // the query branch of Negotiate would have made this flag silently disable the culture switcher.
        var options = Options("en", "hu");
        options.UseQueryString = false;

        Assert.True(RaskCultureNegotiator.TrySelect("hu", options, out var selected));
        Assert.Equal("hu", selected.Culture.Name);
    }

    [Fact]
    public void A_UI_language_list_can_differ_from_the_formatting_list()
    {
        // Formats for Austrian German, but the app's text only exists in German.
        var options = Options("en", "de-AT");
        options.SupportedUICultures = ["en", "de"];

        var result = RaskCultureNegotiator.Negotiate("de-AT", null, null, options);
        Assert.Equal("de-AT", result.Culture.Name);
        Assert.Equal("de", result.UICulture.Name);
    }

    [Theory]
    [InlineData("c=hu-HU|uic=hu-HU", "hu-HU", "hu-HU")]
    [InlineData("c=hu|uic=en", "hu", "en")]
    [InlineData("hu-HU", "hu-HU", "hu-HU")]
    [InlineData("uic=hu", "hu", "hu")]
    public void The_cookie_format_round_trips_including_the_bare_tag_a_person_would_type(
        string value, string culture, string uiCulture)
    {
        Assert.True(RaskCultureCookie.TryParse(value, out var parsedCulture, out var parsedUI));
        Assert.Equal(culture, parsedCulture);
        Assert.Equal(uiCulture, parsedUI);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("c=|uic=")]
    public void A_malformed_cookie_is_a_miss_rather_than_a_throw(string? value) =>
        Assert.False(RaskCultureCookie.TryParse(value, out _, out _));

    [Fact]
    public void Format_writes_what_ASP_NET_reads()
    {
        Assert.Equal("c=hu-HU|uic=hu-HU", RaskCultureCookie.Format("hu-HU"));
        Assert.Equal("c=hu-HU|uic=en-US", RaskCultureCookie.Format("hu-HU", "en-US"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a culture")]
    public void An_unusable_culture_name_answers_false_rather_than_throwing(string? name) =>
        Assert.False(RaskCultureResolver.TryResolve(name, out _));

    [Fact]
    public void A_wellformed_but_invented_tag_resolves_and_that_is_fine()
    {
        // Worth pinning because it surprises people: .NET does NOT reject an unknown-but-well-formed
        // tag. ICU manufactures a culture for it, so TryResolve says yes to "zz-ZZ-not-real".
        //
        // It is not a hole. Nothing in Rask trusts a name from outside — negotiation only ever matches
        // against the list the APP configured, so an invented tag from a query string or a cookie can
        // never select anything unless the app itself configured that tag.
        Assert.True(RaskCultureResolver.TryResolve("zz-ZZ-not-real", out _));

        var options = Options("en", "hu");
        Assert.Equal("en", RaskCultureNegotiator.Negotiate("zz-ZZ-not-real", null, null, options).Culture.Name);
        Assert.False(RaskCultureNegotiator.TrySelect("zz-ZZ-not-real", options, out _));
    }
}
