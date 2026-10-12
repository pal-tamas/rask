using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     A layout whose breadcrumb names the list as plain words on the list's own page and as a link back to it
///     on the form's: one KEYED item whose inner element is a <c>div</c> on one page and an <c>a</c> on the
///     other, in the header, while the page in the outlet changes too — two changes of shape in one navigation,
///     far apart, with a menu of eighty-four links between them that does not change at all.
/// </summary>
public sealed partial class TrailApp : Component
{
    protected override Component? HeadAssets => [Meta.Charset("utf-8"), Markup.Title["Trail"]];

    protected override Component? Render() => Router;
}

[Route("/trail")]
public sealed partial class TrailLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } title ? title + " | Trail" : "Trail"];

    protected override Component? Render() =>
        Div.Id("layout")[
            Ui.Sidebar[
                Ui.SidebarNav[
                    Enumerable.Range(1, 84).Select(n => (Component)Ui.SidebarItem.Key(n).Href($"/trail/menu/{n}")[$"Menu {n}"]).ToArray()
                ]
            ],
            Ui.Header[
                Input.Value("").Name("search").Id("search"),
                Ui.Breadcrumbs[
                    Ui.BreadcrumbsItem.Key("home").Href(Routes.TrailListPage()).Icon(Ui.IconName.Home),
                    route.Title == TrailNewPage.Name
                        ? Ui.BreadcrumbsItem.Key("list").Href(Routes.TrailListPage())[TrailListPage.Name]
                        : Ui.BreadcrumbsItem.Key("list")[TrailListPage.Name],
                    route.Title == TrailNewPage.Name ? Ui.BreadcrumbsItem.Key("here")[TrailNewPage.Name] : null
                ]
            ],
            Ui.Main[Outlet]
        ];
}

[Route("orders")]
[ParentRoute(typeof(TrailLayout))]
public sealed partial class TrailListPage : Component
{
    internal const string Name = "Orders";

    protected override string? PageTitle => Name;

    protected override Component? Render() =>
    [
        Div.Class("mb-6")[Ui.Heading.Level(1).Xl[Name], Ui.Button.Href(Routes.TrailNewPage()).Primary["New order"]],
        Ui.Text.Id("rows")["Three orders."],
    ];
}

[Route("orders/new")]
[ParentRoute(typeof(TrailLayout))]
public sealed partial class TrailNewPage : Component
{
    internal const string Name = "New order";

    protected override string? PageTitle => Name;

    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl.Class("mb-6")[Name],
        Ui.Input.Value("").Label("Name"),
        Ui.Button.Id("save").Primary.OnClick(static () => { })["Save"],
    ];
}
