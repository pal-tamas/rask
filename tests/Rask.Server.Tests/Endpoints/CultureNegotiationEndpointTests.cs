using System.Globalization;
using System.Net.Http.Headers;
using Rask.Core;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Endpoints;

// The server half of culture: what the FIRST response says, before any socket exists. This is the part
// no client-side mechanism can do — by the time JavaScript could read navigator.language, the page has
// already been painted in some language, and if it were the wrong one the visitor would see it flash.
public class CultureNegotiationEndpointTests
{
    private static RaskTestHost Host() =>
        RaskTestHost.Create<CulturePage>(configureCulture: c =>
        {
            c.SupportedCultures.Add("en");
            c.SupportedCultures.Add("hu");
            c.SupportedCultures.Add("ar");
        });

    [Fact]
    public async Task Accept_Language_reaches_the_very_first_rendered_html()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("hu"));

        var html = await host.Http.GetStringAsync("/");

        // Not "the page corrects itself afterwards" — the first bytes off the server are already Hungarian.
        Assert.Contains("lang=\"hu\"", html, StringComparison.Ordinal);
        Assert.Contains("<p>hu</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quality_values_decide_which_language_wins()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("hu", 0.3));
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.9));

        Assert.Contains("lang=\"en\"", await host.Http.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_language_the_visitor_refused_is_not_served_to_them()
    {
        // q=0 means "explicitly not this one", which is different from "did not mention it".
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("hu", 0));

        Assert.Contains("lang=\"en\"", await host.Http.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_url_beats_the_header_and_is_remembered_so_a_shared_link_sticks()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var response = await host.Http.GetAsync("/?culture=hu");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("lang=\"hu\"", html, StringComparison.Ordinal);

        // Without persisting here, a shared ?culture=hu link would switch exactly one page load and then
        // snap back on the next navigation — which reads as the feature being broken.
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            v => v.StartsWith(".AspNetCore.Culture", StringComparison.Ordinal));
        // URL-encoded on the wire (c%3Dhu%7Cuic%3Dhu), which is exactly what ASP.NET's own
        // CookieRequestCultureProvider writes. Asserted in the encoded form deliberately: this is the
        // byte sequence both halves have to agree on, and the browser writer must match it — rask-api.js
        // encodes with encodeURIComponent and decodes on read, so a cookie set by either side is
        // readable by the other. A mismatch here would surface as "the language resets on reload".
        Assert.Contains("c%3Dhu%7Cuic%3Dhu", cookie, StringComparison.Ordinal);

        // And it must stay readable from script: the WASM host reads it before its runtime boots.
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_cookie_the_server_writes_is_the_cookie_the_server_reads()
    {
        // Closes the round trip the assertion above only half-proves. The encoding is invisible to
        // Request.Cookies, so this is what guarantees a persisted choice actually survives.
        using var host = Host();

        var first = await host.Http.GetAsync("/?culture=hu");
        var setCookie = Assert.Single(
            first.Headers.GetValues("Set-Cookie"),
            v => v.StartsWith(".AspNetCore.Culture", StringComparison.Ordinal));

        using var replay = Host();
        replay.Http.DefaultRequestHeaders.Add("Cookie", setCookie.Split(';')[0]);

        Assert.Contains("lang=\"hu\"", await replay.Http.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_remembered_choice_beats_the_browsers_preference()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
        host.Http.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c=hu|uic=hu");

        Assert.Contains("lang=\"hu\"", await host.Http.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_right_to_left_language_sets_dir_and_the_others_leave_it_off()
    {
        using var host = Host();

        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("ar"));
        var arabic = await host.Http.GetStringAsync("/");
        Assert.Contains("dir=\"rtl\"", arabic, StringComparison.Ordinal);

        using var latin = Host();
        latin.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("hu"));
        Assert.DoesNotContain("dir=", await latin.Http.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_response_declares_what_it_varied_on()
    {
        using var host = Host();
        var response = await host.Http.GetAsync("/");

        var vary = string.Join(",", response.Headers.Vary);
        Assert.Contains("Accept-Language", vary, StringComparison.Ordinal);
        Assert.Contains("Cookie", vary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_configures_no_languages_renders_exactly_what_it_did_before()
    {
        // The guarantee that makes this feature safe to land: an app that never asked for localization
        // must produce byte-identical HTML. lang stays the literal "en" rather than becoming the
        // machine's "en-US", and no dir attribute appears at all.
        using var host = RaskTestHost.Create<CulturePage>();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("hu"));

        var html = await host.Http.GetStringAsync("/");

        Assert.Contains("lang=\"en\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("dir=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unsupported_language_falls_back_rather_than_being_honoured()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("ja-JP"));

        Assert.Contains("lang=\"en\"", await host.Http.GetStringAsync("/"), StringComparison.Ordinal);
    }

    // Renders the negotiated language into the body, so a test can see the culture the SESSION got
    // rather than only the one the document declares.
    private sealed class CulturePage : Component
    {
        protected override Component Render() => Div[P[UICulture.Name]];
    }
}
