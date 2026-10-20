using Rask.Core.Routing;
using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where the kit's Feedback page was: its components each have a page in the UI tab now, so this sends the reader
///     on to the first of them.
/// </summary>
/// <remarks>
///     The static host cannot answer with a 301, so the old URL keeps a page of its own: it names the progress bar's
///     page canonical, which also keeps it out of the sitemap, goes there as it mounts, and links to every page that
///     took a component, for a reader whose browser runs no script.
/// </remarks>
[Route("ui/feedback")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UiKitFeedbackPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Progress.Meta();

    /// <inheritdoc />
    protected override async Task OnFirstRender() => Go.To(PageMeta.LinkTo(UiDocsCatalog.Progress.Path)).Replacing();

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl["Feedback"],
        Ui.Text.Class("mt-2")["The callout, progress bar, skeleton, toast and tooltip each have a page of their own "
                              + "in the UI tab now."],
        UiKitMovedPages.Pages([
            UiDocsCatalog.Callout, UiDocsCatalog.Progress, UiDocsCatalog.Tooltip, UiDocsCatalog.Skeleton,
            UiDocsCatalog.Toast,
        ])
    ];
}
