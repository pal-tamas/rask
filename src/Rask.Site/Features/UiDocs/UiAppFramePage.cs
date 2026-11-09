using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's app frame — not a Flux component, an addition the owner approved — its demo with its code, and its
///     reference.
/// </summary>
/// <remarks>
///     A frame is a whole document — the sidebar's states are page-wide ids and its pages nest under it — so the demo is
///     an app of its own, framed.
/// </remarks>
[Route("app-frame")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiAppFramePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.AppFrame.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.AppFrame)
            .Lead("An application's frame in one line: the menu in Flux's sidebar, breadcrumbs written from that menu, who is "
                  + "signed in with a sign-out that asks first, and the page in the main. Not one of Flux UI's components: "
                  + "the kit's own, laid out of Flux's sidebar, header, breadcrumbs and profile as its sidebar layout lays "
                  + "them out.")
            .Api(UiApi.AppFrame)[
            UiDocsExample.Key("ui-app-frame").Heading("An app's layout").File("AppFrameExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.AppFrameHomeDemoPage()).Title("An app drawn in Ui.AppFrame")).Testid("ui-app-frame")
                .Blurb("The layout is the route its pages nest under; Outlet is where each page goes. Open Drivers, choose "
                       + "Active, then a driver: the row stays lit beneath its list, and the trail's Drivers leads back to the "
                       + "list still filtered. Vehicles sits in a group, which the trail names. On a phone the menu slides "
                       + "in from the header's toggle and the trail keeps its last two steps.")
                .After([
                    "The look is the kit's, the same in every app: the accent is the app's theme (`--color-accent` in its "
                    + "stylesheet) and the logo is `Logo(url)`.",
                    "Lit and opened by the frame: the row of the page shown, or of the list it is beneath (`/drivers/7` "
                    + "lights Drivers), and the group holding it. A row's own `.Current(…)` wins; a string link — an old "
                    + "front end — is never lit. Home is the row every other row is beneath.",
                    "The way back to a list keeps its filters, sort and page as the reader left them, and leaves out a "
                    + "`Ui.EditorDialog`'s key, so it never reopens a form.",
                    "It mounts `Ui.Toast`, `Ui.ConfirmLeave` and `Ui.Confirm` in the catalog's words, and the built-in "
                    + "404 and 403 stand in its main.",
                ])
        ];
}
