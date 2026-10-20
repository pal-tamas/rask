namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BreadcrumbsDropdownExample : Component
{
    protected override Component? Render() =>
        Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Key("home").Href("#").Icon(Ui.IconName.Home),
            Ui.BreadcrumbsItem.Key("more")[
                Ui.Dropdown[
                    Ui.Button.Ghost.Sm.Icon(Ui.IconName.EllipsisHorizontal).AriaLabel("The folded steps"),
                    Ui.Navmenu[
                        Ui.NavmenuItem.Href("#")["Client"],
                        Ui.NavmenuItem.Href("#")["Team"],
                        Ui.NavmenuItem.Href("#")["User"]
                    ]
                ]
            ],
            Ui.BreadcrumbsItem.Key("post")["Post"]
        ];
}
