using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where the kit's Layout page was: the layouts and its other components each have a page in the UI tab now, so
///     this sends the reader on to the first of them.
/// </summary>
/// <remarks>
///     The static host cannot answer with a 301, so the old URL keeps a page of its own: it names the header layout's
///     page canonical, which also keeps it out of the sitemap, goes there as it mounts, and links to every page that
///     took a component, for a reader whose browser runs no script.
/// </remarks>
[Route("ui/layout")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitLayoutPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Header.Meta();

    /// <inheritdoc />
    protected override async Task OnFirstRender() => Go.To(PageMeta.LinkTo(UiDocsCatalog.Header.Path)).Replacing();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl["Layout & flyouts"],
        Ui.Text.Class("mt-2")["The layouts, the flyout and the rest of this page each have a page of their own in the UI tab now."],
        UiKitMovedPages.Pages([
            UiDocsCatalog.Header, UiDocsCatalog.Sidebar, UiDocsCatalog.Modal, UiDocsCatalog.Avatar,
            UiDocsCatalog.Separator, UiDocsCatalog.Heading, UiDocsCatalog.Text,
        ])
    ];
}
