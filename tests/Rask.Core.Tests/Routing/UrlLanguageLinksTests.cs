using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// What an app with languages in its URLs sees: /en/about is /about's page, a generated link keeps the language of
// the page it is on, .In("de") asks for another, and route.In("de") is the page being shown in it.
public partial class UrlLanguageLinksTests : global::Rask.Core.RaskMarkup
{
    private static readonly Route[] Table = [Route.To<AboutPage>("/about"), Route.To<HomePage>("/")];

    private static (RouteState State, IServiceProvider Services) Session(string path, string languages = "hu,en,de")
    {
        UrlLanguages.IsEnabled = true;
        var state = new RouteState
        {
            Languages = UrlLanguagesTests.Languages(languages.Split(',')),
            Table = () => Table,
        };
        state.Path = path;
        var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        return (state, services);
    }

    private static string Render(Func<Component> body, IServiceProvider services) =>
        new StubComponent(body).RenderAsLiveRoot(services);

    [Fact]
    public void A_prefixed_address_renders_the_page_its_route_declares()
    {
        var (_, services) = Session("/en/about");

        var html = Render(() => Router.Routes(Table), services);

        Assert.Equal("<span>about</span>", html);
    }

    [Fact]
    public void The_default_languages_page_is_at_the_root()
    {
        var (_, services) = Session("/about");

        var html = Render(() => Router.Routes(Table), services);

        Assert.Equal("<span>about</span>", html);
    }

    [Fact]
    public void A_generated_link_keeps_the_language_of_the_page_it_is_on()
    {
        var (_, services) = Session("/de/");

        var html = Render(() => A.Href(Url())["About"], services);

        Assert.Contains("href=\"/de/about\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generated_link_on_a_default_language_page_has_no_prefix()
    {
        var (_, services) = Session("/");

        var html = Render(() => A.Href(Url())["About"], services);

        Assert.Contains("href=\"/about\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("de", "/de/about")]
    [InlineData("hu", "/about")]
    [InlineData("ja", "/about")]
    public void A_link_asked_for_in_a_language_is_in_that_language(string language, string expected)
    {
        var (_, services) = Session("/en/");

        var html = Render(() => A.Href(Url().In(language))["About"], services);

        Assert.Contains($"href=\"{expected}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_converted_to_a_route_url_is_used_as_written()
    {
        var (_, services) = Session("/en/");
        RouteUrl written = "/about";

        var html = Render(() => A.Href(written)["About"], services);

        Assert.Contains("href=\"/about\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_being_shown_in_another_language_keeps_its_path_and_query()
    {
        var (state, services) = Session("/en/about");
        state.Query = new QueryCollection(new Dictionary<string, StringValues> { ["tab"] = "2" });

        var html = Render(() => Div[A.Href(state.In("de"))["DE"], A.Href(state.In("hu"))["HU"]], services);

        Assert.Contains("href=\"/de/about?tab=2\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/about?tab=2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_being_shown_carries_its_page_type_so_a_kit_link_routes_it_in_the_app()
    {
        var (state, _) = Session("/en/about");

        var url = state.In("de");

        Assert.Equal(typeof(AboutPage), url.PageType);
    }

    [Fact]
    public void A_nav_link_to_the_page_being_shown_is_active_under_its_prefix()
    {
        var (_, services) = Session("/en/about");

        var html = Render(() => NavLink.Href(Url())["About"], services);

        Assert.Contains("class=\"active\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/en/about\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigating_to_a_generated_url_moves_the_address_in_the_current_language()
    {
        var (state, _) = Session("/en/");
        var navigator = new Navigator(state);

        using (navigator.EnterHandler())
        {
            navigator.NavigateTo(Url());
        }

        Assert.Equal("/en/about", state.Path);
    }

    [Fact]
    public void Navigating_to_a_url_in_another_language_moves_the_address_into_it()
    {
        var (state, _) = Session("/en/about");
        var navigator = new Navigator(state);

        using (navigator.EnterHandler())
        {
            navigator.NavigateTo(state.In("hu"));
        }

        Assert.Equal("/about", state.Path);
    }

    [Fact]
    public void An_app_that_keeps_languages_out_of_its_urls_writes_links_unchanged()
    {
        UrlLanguages.IsEnabled = true;
        var state = new RouteState { Path = "/" };
        var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();

        var html = Render(() => A.Href(Url().In("de"))["About"], services);

        Assert.Contains("href=\"/about\"", html, StringComparison.Ordinal);
    }

    // What the generated Routes.AboutPage() returns.
    private static RouteUrl Url() => new("/about", null, typeof(AboutPage));

    [SkipFactory]
    public sealed class AboutPage : Component
    {
        protected override Component? Render() => Span["about"];
    }

    [SkipFactory]
    public sealed class HomePage : Component
    {
        protected override Component? Render() => Span["home"];
    }
}
