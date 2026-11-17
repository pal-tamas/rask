using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Globalization;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Routing;

/// <summary>
///     <c>UrlPrefix = true</c> on the Server host (#1278): the default language at the root, the others under their
///     tag, the routes matched without it — on the first request, on a live navigation and on a rebuilt session — and
///     the address deciding the session's language.
/// </summary>
public sealed class UrlLanguagesOnTheServerTests
{
    private static RaskTestHost Host() =>
        RaskTestHost.Create<UrlLanguageApp>(configureCulture: c =>
        {
            c.SupportedCultures.Add("en");
            c.SupportedCultures.Add("de");
            c.UrlPrefix = true;
        });

    [Theory]
    [InlineData("/ulp/about", "en")]
    [InlineData("/de/ulp/about", "de")]
    public async Task A_page_answers_its_address_in_every_language_and_in_that_language(string address, string language)
    {
        using var host = Host();

        var response = await host.Http.GetAsync(address, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"lang=\"{language}\"", html, StringComparison.Ordinal);
        Assert.Contains($"shown-in-{language}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_address_wins_over_the_cookie_and_the_browser()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("de"));
        host.Http.DefaultRequestHeaders.Add("Cookie", ".AspNetCore.Culture=c%3Dde%7Cuic%3Dde");

        var html = await host.Http.GetStringAsync("/ulp/about", TestContext.Current.CancellationToken);

        Assert.Contains("lang=\"en\"", html, StringComparison.Ordinal);
        Assert.Contains("shown-in-en", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_visit_to_the_bare_root_in_another_language_is_sent_on_to_its_root()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("de"));

        var response = await host.Http.GetAsync("/?tab=2", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/de?tab=2", response.Headers.Location?.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task A_first_visit_to_the_bare_root_in_the_default_language_is_served_there()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var response = await host.Http.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("lang=\"en\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_languages_root_is_the_root_page_in_that_language()
    {
        using var host = Host();
        host.Http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));

        var root = await host.Http.GetAsync("/", TestContext.Current.CancellationToken);
        var response = await host.Http.GetAsync("/de", TestContext.Current.CancellationToken);

        // The same page as the root's — this app's root is a layout, so its not-found — in German.
        Assert.Equal(root.StatusCode, response.StatusCode);
        Assert.Contains("lang=\"de\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_link_on_a_page_keeps_its_language_and_a_switcher_names_the_others()
    {
        using var host = Host();

        var html = await host.Http.GetStringAsync("/de/ulp/about?tab=2", TestContext.Current.CancellationToken);

        Assert.Contains("id=\"contact\" href=\"/de/ulp/contact\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"in-en\" href=\"/ulp/about?tab=2\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"in-de\" href=\"/de/ulp/about?tab=2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_served_head_names_the_page_in_every_language()
    {
        using var host = Host();

        var html = await host.Http.GetStringAsync("/de/ulp/about", TestContext.Current.CancellationToken);

        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/de/ulp/about\">", head, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"en\" href=\"http://localhost/ulp/about\">", head, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"de\" href=\"http://localhost/de/ulp/about\">", head, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"alternate\" hreflang=\"x-default\" href=\"http://localhost/ulp/about\">", head, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_page_that_is_not_found_names_no_other_languages()
    {
        using var host = Host();

        var response = await host.Http.GetAsync("/de/ulp/missing", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("hreflang", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_without_url_languages_matches_a_language_segment_as_any_other()
    {
        using var host = RaskTestHost.Create<UrlLanguageApp>(configureCulture: c =>
        {
            c.SupportedCultures.Add("en");
            c.SupportedCultures.Add("de");
        });

        var response = await host.Http.GetAsync("/de/ulp/about", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("hreflang", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_navigation_from_a_handler_keeps_the_prefix_of_the_page()
    {
        await using var live = await Live.Open("/de/ulp/about");

        await live.Click("go-contact");
        await live.Shows("contact-in-de");

        Assert.Equal("/de/ulp/contact", live.Route.Path);
    }

    [Fact]
    public async Task Following_a_link_to_another_language_switches_the_sessions_culture_with_it()
    {
        await using var live = await Live.Open("/de/ulp/about");

        await live.Navigate("/ulp/contact");
        await live.Shows("contact-in-en");

        Assert.Equal("en", live.Culture.UICulture.Name);
        Assert.Equal("/ulp/contact", live.Route.Path);
    }

    [Fact]
    public async Task A_live_navigation_to_a_prefixed_address_matches_its_page()
    {
        await using var live = await Live.Open("/ulp/about");

        await live.Navigate("/de/ulp/contact");
        await live.Shows("contact-in-de");

        Assert.Equal("de", live.Culture.UICulture.Name);
    }

    [Theory]
    [InlineData("/de/ulp/about", "/ulp/about")]
    [InlineData("/de", "/")]
    [InlineData("/ulp/about", "/ulp/about")]
    [InlineData("/fr/ulp/about", "/fr/ulp/about")]
    public void The_routes_are_matched_without_the_language_prefix(string address, string routePath)
    {
        var languages = new ServerUrlLanguages(Options(urlPrefix: true));

        var matched = languages.RoutePathOf(address);

        Assert.Equal(routePath, matched);
    }

    [Theory]
    [InlineData("/de/ulp/about", "/login?returnUrl=x", "/de/login?returnUrl=x")]
    [InlineData("/ulp/about", "/login", "/login")]
    public void Where_a_guard_sends_a_reader_keeps_the_language_of_the_page_they_asked_for(string address, string url, string kept)
    {
        var languages = new ServerUrlLanguages(Options(urlPrefix: true));

        var sentTo = languages.Keep(address, url);

        Assert.Equal(kept, sentTo);
    }

    [Fact]
    public void A_host_that_keeps_languages_out_of_its_urls_leaves_every_address_as_it_is()
    {
        var languages = new ServerUrlLanguages(Options(urlPrefix: false));

        var routePath = languages.RoutePathOf("/de/ulp/about");
        var kept = languages.Keep("/de/ulp/about", "/login");

        Assert.Null(languages.Languages);
        Assert.Equal("/de/ulp/about", routePath);
        Assert.Equal("/login", kept);
    }

    private static RaskCultureOptions Options(bool urlPrefix)
    {
        var options = new RaskCultureOptions { UrlPrefix = urlPrefix };
        options.SupportedCultures.Add("en");
        options.SupportedCultures.Add("de");
        return options;
    }

    private sealed class Live(RaskTestHost host, WebSocket ws, string html, LiveSession session) : IAsyncDisposable
    {
        public RouteState Route => session.Services.GetRequiredService<RouteState>();

        public IRaskCulture Culture => session.Services.GetRequiredService<IRaskCulture>();

        public static async Task<Live> Open(string address)
        {
            var host = Host();
            var html = await (await host.Http.GetAsync(address)).Content.ReadAsStringAsync();
            var sessionId = MarkupAssert.SessionId(html);
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            await ws.AttachedAsync(host, sessionId);
            return new Live(host, ws, html, host.Store.Get(sessionId)!);
        }

        public Task Click(string buttonId)
        {
            var handler = Regex.Match(
                html, $"id=\"{Regex.Escape(buttonId)}\"[^>]*data-rask-on-click=\"([^\"]+)\"",
                RegexOptions.None, TimeSpan.FromSeconds(1));
            return ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "click" });
        }

        public Task Navigate(string path) => ws.SendJsonAsync(new { type = "navigate", path, query = "" });

        public Task Shows(string text) =>
            ws.ReceiveUntilAsync(frame => frame.Contains(text, StringComparison.Ordinal), $"a frame showing {text}");

        public async ValueTask DisposeAsync()
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }

            ws.Dispose();
            host.Dispose();
        }
    }
}

public sealed partial class UrlLanguageApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/ulp/about")]
public sealed partial class UrlLanguageAboutPage(RouteState route) : Component
{
    protected override Component? Render() =>
        Div[
            P.Id("about")[$"shown-in-{UICulture.Name}"],
            A.Id("contact").Href(Routes.UrlLanguageContactPage())["contact"],
            A.Id("in-en").Href(route.In("en"))["English"],
            A.Id("in-de").Href(route.In("de"))["Deutsch"],
            Button.Id("go-contact").OnClick(() => Routes.UrlLanguageContactPage().Go())["go"]
        ];
}

[Route("/ulp/contact")]
public sealed partial class UrlLanguageContactPage : Component
{
    protected override Component? Render() => P.Id("contact-page")[$"contact-in-{UICulture.Name}"];
}
