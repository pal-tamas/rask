using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's sidebar layout in C#: each demo with its own code, and the reference of its parts.</summary>
[Route("sidebar")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiSidebarPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Sidebar.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Sidebar)
            .Lead("Use a sidebar navigation layout to keep your application content front and center.")
            .Api(UiApi.Sidebar)[
            UiDocsExample.Key("ui-sidebar").Heading("Sidebar").File("SidebarLayoutExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.SidebarLayoutDemoPage()).Title("The sidebar layout")).Testid("ui-sidebar")
                .Blurb("Docked beside the main on a desktop. On a phone the header's toggle slides it over the page."),
            UiDocsExample.Key("ui-sidebar-inset").Heading("Inset main").File("SidebarInsetExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.SidebarInsetDemoPage()).Title("The sidebar layout with an inset main")).Testid("ui-sidebar-inset")
                .Blurb("Add `Inset(true)` to `Ui.Main` to place your content in a bordered panel with rounded corners. "
                       + "On large screens, content scrolls inside the panel, keeping its border and the space around "
                       + "it visible. On smaller screens, the panel becomes full width and the page scrolls normally.")
                .After([
                    "Set the background around the panel on your layout and sidebar. Add `lg:ms-0` to the main to "
                    + "remove the extra margin beside the sidebar.",
                    "Keep the header, sidebar, and main as direct siblings so the panel can adjust to the header's "
                    + "height. If your layout includes a header, use `lg:ms-0 lg:mt-0` to also remove the margin below it.",
                ]),
            UiDocsExample.Key("ui-sidebar-header").Heading("Secondary header").File("SidebarSecondaryHeaderExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.SidebarWithHeaderDemoPage()).Title("The sidebar layout with a secondary header")).Testid("ui-sidebar-header")
                .Blurb("Use a top header for secondary navigation."),
            UiDocsExample.Key("ui-sidebar-collapsible").Heading("Collapsible sidebar").File("SidebarCollapsibleExample.cs")
                .Demo(UiLayoutPreview.Demo(Routes.SidebarCollapsibleDemoPage()).Title("The collapsible sidebar layout")).Testid("ui-sidebar-collapsible")
                .Blurb("Make your app feel more spacious with a collapsible sidebar."),
            UiSidebarBehaviour
        ];
}
