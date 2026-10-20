using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where the kit's Data display page was: its components each have a page in the UI tab now, so this sends the
///     reader on to the first of them.
/// </summary>
/// <remarks>
///     The static host cannot answer with a 301, so the old URL keeps a page of its own: it names the accordion's page
///     canonical, which also keeps it out of the sitemap, goes there as it mounts, and links to every page that took a
///     component, for a reader whose browser runs no script. It also answers the tree's old page: the tree left the kit,
///     and an accordion of items is what a nested list is drawn with now.
/// </remarks>
[Route("ui/data-display")]
[Route("ui/tree")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitDataDisplayPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Accordion.Meta();

    /// <inheritdoc />
    protected override async Task OnFirstRender() => Go.To(PageMeta.LinkTo(UiDocsCatalog.Accordion.Path)).Replacing();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl["Data display"],
        Ui.Text.Class("mt-2")["The accordion, avatar, badge, card, carousel, chart, flag, kanban, table and timeline each "
                              + "have a page of their own in the UI tab now."],
        UiKitMovedPages.Pages([
            UiDocsCatalog.Accordion, UiDocsCatalog.Avatar, UiDocsCatalog.Badge, UiDocsCatalog.Card, UiDocsCatalog.Carousel,
            UiDocsCatalog.Chart, UiDocsCatalog.Flag, UiDocsCatalog.Kanban, UiDocsCatalog.Table, UiDocsCatalog.Timeline,
        ])
    ];
}
