using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/breadcrumbs</c>, example for example.
/// </summary>
/// <remarks>
///     The last example folds the middle of the trail into a dropdown, which is another page's component: one
///     box of the size of Flux's small ghost button, inside the item that holds it.
/// </remarks>
public sealed partial class BreadcrumbsParity : FluxParity
{
    public override string Page => "breadcrumbs";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Row(Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Href("#")["Home"],
            Ui.BreadcrumbsItem.Href("#")["Blog"],
            Ui.BreadcrumbsItem["Post"]
        ]));

        yield return ("with-slashes", Row(Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Href("#").Separator(Ui.IconName.Slash)["Home"],
            Ui.BreadcrumbsItem.Href("#").Separator(Ui.IconName.Slash)["Blog"],
            Ui.BreadcrumbsItem.Separator(Ui.IconName.Slash)["Post"]
        ]));

        yield return ("with-icon", Row(Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Href("#").Icon(Ui.IconName.Home),
            Ui.BreadcrumbsItem.Href("#")["Blog"],
            Ui.BreadcrumbsItem["Post"]
        ]));

        yield return ("with-ellipsis", Row(Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Href("#").Icon(Ui.IconName.Home),
            Ui.BreadcrumbsItem.Icon(Ui.IconName.EllipsisHorizontal),
            Ui.BreadcrumbsItem["Post"]
        ]));

        yield return ("with-ellipsis-dropdown", Row(Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Href("#").Icon(Ui.IconName.Home),
            Ui.BreadcrumbsItem[
                Ui.Dropdown[
                    Ui.Button.Ghost.Sm.Icon(Ui.IconName.EllipsisHorizontal),
                    Ui.Navmenu[
                        Ui.NavmenuItem["Client"],
                        Ui.NavmenuItem.Icon(Ui.IconName.ArrowTurnDownRight)["Team"],
                        Ui.NavmenuItem.Icon(Ui.IconName.ArrowTurnDownRight)["User"]
                    ]
                ]
            ],
            Ui.BreadcrumbsItem["Post"]
        ]));
    }
}
