using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where the kit's Navigation page was: its components each have a page in the UI tab now, so this sends the
///     reader on to the first of them.
/// </summary>
/// <remarks>
///     The static host cannot answer with a 301, so the old URL keeps a page of its own: it names the brand's page
///     canonical, which also keeps it out of the sitemap, goes there as it mounts, and links to every page that took a
///     component, for a reader whose browser runs no script.
/// </remarks>
[Route("ui/navigation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitNavigationPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Brand.Meta();

    /// <inheritdoc />
    protected override async Task OnFirstRender() => Go.To(PageMeta.LinkTo(UiDocsCatalog.Brand.Path)).Replacing();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl["Navigation"],
        Ui.Text.Class("mt-2")["The brand, breadcrumbs, navbar, navlist, pagination, profile and tabs each have a page "
                              + "of their own in the UI tab now."],
        UiKitMovedPages.Pages([
            UiDocsCatalog.Tabs, UiDocsCatalog.Navlist, UiDocsCatalog.Breadcrumbs, UiDocsCatalog.Pagination,
            UiDocsCatalog.Navbar, UiDocsCatalog.Brand, UiDocsCatalog.Profile, UiDocsCatalog.Avatar,
        ])
    ];
}
