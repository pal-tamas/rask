using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where the kit's Actions page was: its components each have a page in the UI tab now, so this sends the reader
///     on to the first of them.
/// </summary>
/// <remarks>
///     The static host cannot answer with a 301, so the old URL keeps a page of its own: it names the dropdown's page
///     canonical, which also keeps it out of the sitemap, goes there as it mounts, and links to every page that took
///     a component, for a reader whose browser runs no script.
/// </remarks>
[Route("ui/actions")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitActionsPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Dropdown.Meta();

    /// <inheritdoc />
    protected override async Task OnFirstRender() => Go.To(UiDocsCatalog.Dropdown.Path).Replacing();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl["Actions"],
        Ui.Text.Class("mt-2")["The menus, the command palette, popovers and modals each have a page of their own in "
                              + "the UI tab now."],
        UiKitMovedPages.Pages([
            UiDocsCatalog.Button, UiDocsCatalog.Dropdown, UiDocsCatalog.Context, UiDocsCatalog.Command,
            UiDocsCatalog.Popover, UiDocsCatalog.Modal,
        ])
    ];
}
