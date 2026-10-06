using Rask.Core.Routing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's link: its three variants, the accent, and the destinations it tells apart.
/// </summary>
public partial class UiLinkTests : global::Rask.Core.RaskMarkup
{
    private static readonly RouteUrl Orders = new("/orders", null, typeof(UiLinkTests));

    [Fact]
    public void A_string_is_an_ordinary_underlined_link_in_the_accent()
    {
        var link = Ui.Link.Href("https://example.test/")["Docs"];

        var html = link.ToHtml();

        Assert.Equal(
            "<a class=\"font-medium underline-offset-[6px] text-fx-accent-content decoration-fx-accent-content/20 "
            + "underline hover:decoration-current\" data-ui-link href=\"https://example.test/\">Docs</a>",
            html);
    }

    [Fact]
    public void A_generated_route_navigates_inside_the_app()
    {
        var link = Ui.Link.Href(Orders)["Orders"];

        var html = link.ToHtml();

        Assert.EndsWith(" data-ui-link href=\"/orders\" data-rask-nav>Orders</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ghost_link_is_underlined_only_under_the_pointer()
    {
        var link = Ui.Link.Href("/x").Ghost["X"];

        var html = link.ToHtml();

        Assert.Contains(" no-underline hover:underline hover:decoration-current\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_subtle_link_is_body_ink_that_darkens_under_the_pointer()
    {
        var link = Ui.Link.Href("/x").Subtle["X"];

        var html = link.ToHtml();

        Assert.Contains("text-zinc-500 dark:text-white/70 hover:text-fx-accent-content no-underline\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_accent_a_link_is_drawn_in_the_pages_ink()
    {
        var link = Ui.Link.Href("/x").Accent(false)["X"];

        var html = link.ToHtml();

        Assert.Contains("text-zinc-800 decoration-zinc-800/20 dark:text-white dark:decoration-white/20", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fx-accent", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_link_opens_away_and_cannot_reach_back()
    {
        var link = Ui.Link.Href("https://example.com").External()["The spec"];

        var html = link.ToHtml();

        Assert.EndsWith(" href=\"https://example.com\" target=\"_blank\" rel=\"noopener noreferrer\">The spec</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generated_route_is_never_external()
    {
        var link = Ui.Link.Href(Orders).External()["Orders"];

        var html = link.ToHtml();

        Assert.DoesNotContain("target=", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-nav", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_as_a_button_is_a_button_that_never_submits()
    {
        var link = Ui.Link.As(Ui.LinkAs.Button).Href("/ignored")["Create new account"];

        var html = link.ToHtml();

        Assert.StartsWith("<button class=\"font-medium ", html, StringComparison.Ordinal);
        Assert.EndsWith(" data-ui-link type=\"button\">Create new account</button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_script_url_is_refused()
    {
        var link = Ui.Link.Href("javascript:alert(1)")["X"];

        var html = link.ToHtml();

        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
    }
}
