using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>The app frame demo's list: filtered by its address, each row a link to the page beneath it.</summary>
[Route("drivers")]
[ParentRoute(typeof(Examples.AppFrameExample))]
public sealed partial class AppFrameDriversDemoPage(RouteState route) : Component
{
    /// <inheritdoc />
    protected override string? PageTitle => "Drivers";

    /// <inheritdoc />
    protected override Component? Render()
    {
        var active = route.Query.TryGetValue("status", out var status) && StringValues.Equals(status, "active");
        return Div.Key("drivers").Class("space-y-6")[
            Ui.Heading.Level(1).Xl["Drivers"],
            Ui.Tabs.Segmented[
                Ui.Tab.Key("all").Selected(!active).OnClick(() => Go.Without("status"))["All"],
                Ui.Tab.Key("active").Selected(active).OnClick(() => Go.With("status", "active"))["Active"]
            ],
            Ui.Table.ContainerClass("min-w-0")[
                Ui.TableColumns[Ui.TableColumn["Driver"], Ui.TableColumn["Licence"]],
                Ui.TableRows[AppFrameDemoDrivers.All.Where(driver => !active || driver.Active).Select(driver =>
                    Ui.TableRow.Key(driver.Id)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[
                            Ui.Link.Href(Routes.AppFrameDriverDemoPage(driver.Id)).Attributes(("data-testid", "af-driver-" + driver.Id))[driver.Name]
                        ],
                        Ui.TableCell[driver.Licence]
                    ])]
            ]
        ];
    }
}
