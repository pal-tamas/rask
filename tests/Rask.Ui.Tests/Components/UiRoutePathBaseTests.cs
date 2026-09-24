using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.UiTests.Components;

[CollectionDefinition(nameof(DeployPathBaseCollection), DisableParallelization = true)]
public sealed class DeployPathBaseCollection;

/// <summary>
///     A kit button or link given a generated route carries the deploy's path base, as <c>NavLink</c> does.
/// </summary>
/// <remarks>
///     A route's own path is prefix-less, so without it a sub-path deploy's link works when clicked — the runtime
///     routes against its own base — and 404s everywhere else: a new tab, a copied link, a crawler (#975).
///     <c>LiveOptions.PathBase</c> is process-wide, and every NavLink-backed kit component in the parallel
///     classes renders from it, so these run on their own.
/// </remarks>
[Collection(nameof(DeployPathBaseCollection))]
public partial class UiRoutePathBaseTests : global::Rask.Core.RaskMarkup
{
    private static readonly RouteUrl Orders = new("/orders", null, typeof(UiRoutePathBaseTests));

    [Fact]
    public void A_routed_button_and_link_carry_the_deploy_path_base() => UnderPathBase("/shop", () =>
    {
        Assert.Contains("href=\"/shop/orders\"", Ui.Button.Href(Orders)["Orders"].ToHtml(), StringComparison.Ordinal);
        Assert.Contains("href=\"/shop/orders\"", Ui.Link.Href(Orders).Text("Orders").ToHtml(), StringComparison.Ordinal);
    });

    [Fact]
    public void A_routed_button_opened_in_a_new_tab_still_carries_it() => UnderPathBase("/shop", () =>
        // The one case the runtime never intercepts is the one that needs the prefix most: the new tab
        // requests the URL from the host.
        Assert.Contains(
            "href=\"/shop/orders\"",
            Ui.Button.Href(Orders).NewTab(true)["Orders"].ToHtml(),
            StringComparison.Ordinal));

    [Fact]
    public void A_string_is_written_as_given() => UnderPathBase("/shop", () =>
    {
        Assert.Contains("href=\"/orders\"", Ui.Button.Href("/orders")["Orders"].ToHtml(), StringComparison.Ordinal);
        Assert.Contains("href=\"/orders\"", Ui.Link.Href("/orders").Text("Orders").ToHtml(), StringComparison.Ordinal);
    });

    // #1070: every kit component that takes a RouteUrl follows Ui.Button and Ui.Link. A generated route navigates in
    // place; a string is an ordinary link, and gets neither the path base nor the runtime's interception.
    public static TheoryData<string> LinkingComponents => ["Ui.Stat", "Ui.Card", "Ui.NavTab", "Ui.Brand"];

    [Theory]
    [MemberData(nameof(LinkingComponents))]
    public void A_routed_kit_link_carries_the_path_base_and_navigates_in_place(string component) =>
        UnderPathBase("/shop", () =>
        {
            var html = Render(component, Orders);
            Assert.Contains("href=\"/shop/orders\"", html, StringComparison.Ordinal);
            Assert.Contains("data-rask-nav", html, StringComparison.Ordinal);
        });

    [Theory]
    [MemberData(nameof(LinkingComponents))]
    public void A_string_kit_link_is_an_ordinary_link_written_as_given(string component) =>
        UnderPathBase("/shop", () =>
        {
            var html = Render(component, "https://status.example.test");
            Assert.Contains("href=\"https://status.example.test\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("/shop", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-rask-nav", html, StringComparison.Ordinal);
        });

    [Fact]
    public void An_active_string_tab_still_says_it_is_the_current_page() => UnderPathBase("/shop", () =>
        Assert.Contains(
            "aria-current=\"page\"",
            Ui.NavTab.Label("Status").Href("https://status.example.test").Active(true).ToHtml(),
            StringComparison.Ordinal));

    private string Render(string component, RouteUrl href) => component switch
    {
        "Ui.Stat" => Ui.Stat.Value("OK").Label("Status").Href(href).ToHtml(),
        "Ui.Card" => Ui.Card.Heading("Status").Href(href)[Span["body"]].ToHtml(),
        "Ui.NavTab" => Ui.NavTab.Label("Status").Href(href).ToHtml(),
        "Ui.Brand" => Ui.Brand.Label("Status").Href(href).ToHtml(),
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };

    private static void UnderPathBase(string pathBase, Action assert)
    {
        var prior = LiveOptions.PathBase;
        try
        {
            LiveOptions.PathBase = pathBase;
            assert();
        }
        finally
        {
            LiveOptions.PathBase = prior;
        }
    }
}
