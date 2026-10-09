using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's navbar and navlist: what each part writes, and how an item learns it is the page being shown.
/// </summary>
public partial class UiNavbarTests : global::Rask.Core.RaskMarkup
{
    private sealed class StubComponent(global::Rask.Core.Component inner) : global::Rask.Core.Component
    {
        protected override global::Rask.Core.Component? Render() => inner;
    }

    private static IDisposable OnPage(string path)
    {
        var services = new ServiceCollection().AddSingleton(new RouteState { Path = path }).BuildServiceProvider();
        return LiveRenderContext.Begin(new StubComponent(Span), services);
    }

    // A generated route carries its page type, which is what lets a NavLink compare it with the page shown.
    private static RouteUrl Route(string path) => new(path, null, typeof(UiNavbarTests));

    [Fact]
    public void A_navbar_is_a_nav_landmark_marked_as_Flux_marks_it()
    {
        var html = Ui.Navbar[Ui.NavbarItem.Href("/")["Home"]].ToHtml();

        Assert.StartsWith("<nav class=\"flex items-center gap-[2px] py-3\" data-ui-navbar>", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-navbar-items", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_navlist_is_a_nav_landmark_too()
    {
        var html = Ui.Navlist[Ui.NavlistItem.Href("/")["Home"]].ToHtml();

        Assert.StartsWith("<nav class=\"flex flex-col\" data-ui-navlist>", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-navlist-item", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_item_for_the_page_being_shown_is_current_without_being_told()
    {
        using var _ = OnPage("/orders");

        var current = Ui.NavbarItem.Href(Route("/orders"))["Orders"].ToHtml();
        var other = Ui.NavbarItem.Href(Route("/customers"))["Customers"].ToHtml();

        Assert.Contains("aria-current=\"page\"", current, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", other, StringComparison.Ordinal);
    }

    [Fact]
    public void A_navlist_item_works_its_current_state_out_the_same_way()
    {
        using var _ = OnPage("/orders");

        var current = Ui.NavlistItem.Href(Route("/orders"))["Orders"].ToHtml();
        var other = Ui.NavlistItem.Href(Route("/customers"))["Customers"].ToHtml();

        Assert.Contains("aria-current=\"page\"", current, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", other, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_can_be_stated_either_way()
    {
        using var _ = OnPage("/orders");

        var off = Ui.NavbarItem.Href(Route("/orders")).Current(false)["Orders"].ToHtml();
        var on = Ui.NavbarItem.Href(Route("/help")).Current(true)["Help"].ToHtml();

        Assert.DoesNotContain("aria-current", off, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", on, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_href_is_an_ordinary_link_whose_current_state_is_stated()
    {
        using var _ = OnPage("/orders");

        var html = Ui.NavbarItem.Href("/orders")["Orders"].ToHtml();
        var stated = Ui.NavbarItem.Href("/orders").Current(true)["Orders"].ToHtml();

        Assert.DoesNotContain("data-rask-nav", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\"", stated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_item_is_drawn_in_the_accent_unless_the_accent_is_turned_off()
    {
        var accent = Ui.NavbarItem.Href("/").Current(true)["Home"].ToHtml();

        var plain = Ui.NavbarItem.Href("/").Current(true).Accent(false)["Home"].ToHtml();

        Assert.Contains("aria-[current=page]:text-fx-accent-content", accent, StringComparison.Ordinal);
        Assert.Contains("aria-[current=page]:after:bg-fx-accent", accent, StringComparison.Ordinal);
        Assert.DoesNotContain("fx-accent", plain, StringComparison.Ordinal);
        Assert.Contains("aria-[current=page]:after:bg-zinc-800", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_with_nowhere_to_go_is_a_button()
    {
        var html = Ui.NavbarItem.IconTrailing(Ui.IconName.ChevronDown)["Account"].ToHtml();

        Assert.StartsWith("<button ", html, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_sits_before_the_words_and_a_badge_after_them()
    {
        var html = Ui.NavbarItem.Href("/inbox").Icon(Ui.IconName.Envelope).Badge("12")["Inbox"].ToHtml();

        var icon = html.IndexOf("<svg", StringComparison.Ordinal);
        var words = html.IndexOf("data-content", StringComparison.Ordinal);
        var badge = html.IndexOf(">12</span>", StringComparison.Ordinal);
        Assert.True(icon >= 0 && icon < words && words < badge, html);
        Assert.Contains("bg-zinc-400/15", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_badge_takes_one_of_the_shared_colours()
    {
        var html = Ui.NavlistItem.Href("/calendar").Badge("Pro").BadgeColor(Ui.Color.Lime)["Calendar"].ToHtml();

        Assert.Contains("data-ui-navlist-badge", html, StringComparison.Ordinal);
        Assert.Contains("text-lime-800 bg-lime-400/25", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_is_a_heading_over_its_items()
    {
        var html = Ui.NavlistGroup.Heading("Account")[Ui.NavlistItem.Href("/profile")["Profile"]].ToHtml();

        Assert.Contains("Account", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<details", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_expandable_group_is_a_disclosure_that_starts_open()
    {
        var html = Ui.NavlistGroup.Heading("Account").Expandable()[Ui.NavlistItem.Href("/profile")["Profile"]].ToHtml();

        Assert.Contains("<details", html, StringComparison.Ordinal);
        Assert.Contains(" open", html, StringComparison.Ordinal);
        Assert.Contains("<summary", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("<svg").Length - 1);
    }

    [Fact]
    public void Expanded_false_starts_it_closed()
    {
        var html = Ui.NavlistGroup.Heading("Account").Expandable().Expanded(false)[Ui.NavlistItem.Href("/p")["P"]].ToHtml();

        var details = html[..html.IndexOf('>', StringComparison.Ordinal)];

        Assert.DoesNotContain(" open", details, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_that_reports_its_state_listens_for_the_toggle()
    {
        var silent = Page.Render(Ui.NavlistGroup.Heading("Account").Expandable()[Div]).Html;

        var heard = Page.Render(Ui.NavlistGroup.Heading("Account").Expandable().OnExpandedChange(_ => { })[Div]).Html;

        Assert.DoesNotContain("toggle", silent, StringComparison.Ordinal);
        Assert.Contains("toggle", heard, StringComparison.Ordinal);
    }
}
