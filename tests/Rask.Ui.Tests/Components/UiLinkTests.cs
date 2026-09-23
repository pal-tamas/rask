using Rask.Core.Routing;

namespace Rask.UiTests.Components;

/// <summary>
///     The themed link: its classes, and the two kinds of destination it tells apart.
/// </summary>
public partial class UiLinkTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_string_is_an_ordinary_link() =>
        Assert.Equal(
            "<a class=\"link link-hover\" href=\"https://example.test/\">Docs</a>",
            Ui.Link.Href("https://example.test/").Text("Docs").ToHtml());

    [Fact]
    public void A_generated_route_navigates_inside_the_app() =>
        // Rendered through NavLink, so the runtime routes the click in place — with no active class, since a
        // link in running text has no "you are here" state to show.
        Assert.Equal(
            "<a class=\"link link-hover\" href=\"/orders\" data-rask-nav>Orders</a>",
            Ui.Link.Href(new RouteUrl("/orders", null, typeof(UiLinkTests))).Text("Orders").ToHtml());

    [Fact]
    public void Tone_and_underline_compose_with_the_callers_class() =>
        Assert.Equal(
            "<a class=\"link link-primary mb-0\" href=\"/x\">X</a>",
            Ui.Link.Href("/x").Text("X").Tone(Ui.Tone.Primary).Underline(false).Class("mb-0").ToHtml());
}
