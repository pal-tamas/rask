namespace Rask.UiTests.Components;

/// <summary>
///     The section tab works out which page it is on, the way the sidebar's item does.
/// </summary>
/// <remarks>
///     It did not, and that asymmetry was a footgun rather than a choice: <c>UiNavItem</c> derived its current
///     state from the route while <c>UiNavTab</c> — the same control in a row rather than a column — silently
///     showed none unless every call site remembered to say so. Nothing failed; the bar simply had no tab marked.
/// </remarks>
public partial class UiNavTabCurrentTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Stated_active_marks_the_tab_for_assistive_tech_too()
    {
        // The underline is not a fact a screen reader can see; aria-current is.
        var html = Ui.NavTab.Label("Logs").Href("/logs").Active(true).ToHtml();

        Assert.Contains("aria-current=\"page\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inactive_tab_claims_nothing()
    {
        // Not `aria-current="false"`, and no data-inactive either: an attribute on every tab of every page is
        // noise that invites someone to start styling off it.
        var html = Ui.NavTab.Label("Logs").Href("/logs").Active(false).ToHtml();

        Assert.DoesNotContain("aria-current", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Unset_it_asks_the_route_rather_than_showing_nothing()
    {
        // A NavLink compares its href with the page being shown and writes both the active class and
        // aria-current. A GENERATED route is what it can do that for — it carries the page type — so that is
        // what this hands it, the way a real call site's Routes.X() would.
        var generated = new global::Rask.Core.Routing.RouteUrl("/logs", null, typeof(UiNavTabCurrentTests));
        var html = Ui.NavTab.Label("Logs").Href(generated).ToHtml();

        Assert.Contains("href=\"/logs\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-nav", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_icon_and_a_badge_sit_either_side_of_the_label()
    {
        // flux:navbar.item has both, and a tab bar without them cannot show a count — the thing a "Logs" or
        // "Errors" tab most wants to say.
        var html = Ui.NavTab.Label("Errors").Href("/errors").Icon(Ui.IconName.Warning).Badge("12")
            .BadgeTone(Ui.Tone.Error).ToHtml();

        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains("badge", html, StringComparison.Ordinal);
        Assert.Contains("12", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_href_stays_an_ordinary_link()
    {
        // #1070: a string is written exactly as given, with no path base added — right for a URL that leaves
        // the app, wrong for one of your own pages. It also has no route to be compared against, so its
        // current state can only be stated.
        var html = Ui.NavTab.Label("Docs").Href("https://example.com/docs").ToHtml();

        Assert.Contains("href=\"https://example.com/docs\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-nav", html, StringComparison.Ordinal);
    }
}
