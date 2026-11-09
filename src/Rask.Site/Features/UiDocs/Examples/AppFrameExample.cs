using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs.Examples;

// An app's layout, whole: the route its pages nest under, and the frame they are drawn in. The menu is one sidebar nav,
// and the frame lights the row of the page shown (or of the list it is beneath), opens its group, and writes the
// breadcrumbs from it. The outlet is where each page goes, and the built-in 404 and 403 with them.
[Route("demo/app-frame")]
public sealed partial class AppFrameExample : Component
{
    protected override Component? HeadAssets => UiLayoutDemo.Meta("App frame", UiDocsCatalog.AppFrame);

    protected override Component? Render() =>
        Ui.AppFrame.Brand("Acme Fuels").Logo("/img/rask-mark.svg").Tenant("Northern depot").User("Mara Lind")
            .SignOut(Routes.UiAppFramePage())[
            Ui.SidebarNav[
                Ui.SidebarItem.Key("home").Icon(Ui.IconName.Home).Href(Routes.AppFrameHomeDemoPage())["Home"],
                Ui.SidebarItem.Key("drivers").Icon(Ui.IconName.Identification).Href(Routes.AppFrameDriversDemoPage())["Drivers"],
                Ui.SidebarGroup.Key("fleet").Expandable(true).Icon(Ui.IconName.Truck).Heading("Fleet")[
                    Ui.SidebarItem.Key("vehicles").Icon(Ui.IconName.Truck).Href(Routes.AppFrameVehiclesDemoPage())["Vehicles"]
                ]
            ],
            Outlet
        ];
}
