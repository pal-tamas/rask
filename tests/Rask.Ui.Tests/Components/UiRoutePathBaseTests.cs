using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Ui.Tests.Components;

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
        Assert.Contains("href=\"/shop/orders\"", UiButton.Href(Orders)["Orders"].ToHtml(), StringComparison.Ordinal);
        Assert.Contains("href=\"/shop/orders\"", UiLink.Href(Orders).Text("Orders").ToHtml(), StringComparison.Ordinal);
    });

    [Fact]
    public void A_routed_button_opened_in_a_new_tab_still_carries_it() => UnderPathBase("/shop", () =>
        // The one case the runtime never intercepts is the one that needs the prefix most: the new tab
        // requests the URL from the host.
        Assert.Contains(
            "href=\"/shop/orders\"",
            UiButton.Href(Orders).NewTab(true)["Orders"].ToHtml(),
            StringComparison.Ordinal));

    [Fact]
    public void A_string_is_written_as_given() => UnderPathBase("/shop", () =>
    {
        Assert.Contains("href=\"/orders\"", UiButton.Href("/orders")["Orders"].ToHtml(), StringComparison.Ordinal);
        Assert.Contains("href=\"/orders\"", UiLink.Href("/orders").Text("Orders").ToHtml(), StringComparison.Ordinal);
    });

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
