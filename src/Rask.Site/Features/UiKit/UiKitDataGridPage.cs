using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where the kit's data grid was: the grid left the kit, and the table is what a list of rows is drawn with now, so
///     a link or a bookmark to it lands on the table's page rather than on a 404.
/// </summary>
/// <remarks>
///     It names the table's page canonical, which keeps it out of the sitemap, goes there as it mounts, and links to it
///     for a reader whose browser runs no script.
/// </remarks>
[Route("ui/data-grid")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataGridPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Table.Meta();

    /// <inheritdoc />
    protected override async Task OnFirstRender() => Go.To(PageMeta.LinkTo(UiDocsCatalog.Table.Path)).Replacing();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl["Data grid"],
        Ui.Text.Class("mt-2")["The data grid left the kit: a table with sortable headings and a pager draws a list of "
                              + "rows now."],
        UiKitMovedPages.Pages([UiDocsCatalog.Table])
    ];
}
