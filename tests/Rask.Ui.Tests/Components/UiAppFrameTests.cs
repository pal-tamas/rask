using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>An app's layout on <c>Ui.AppFrame</c>: the shape TA's and Eszaf's layouts take.</summary>
[Route("/ui-app-frame")]
public sealed partial class AppFrameLayout : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.AppFrame.Brand("PPTA").Tenant("Acme Fuels").User("Ada Lovelace").SignOut("/sign-out").Notices(Span.Id("notices")["2 running"])[
            Ui.SidebarNav[
                Ui.SidebarItem.Key("home").Icon(Ui.IconName.Home).Href(Routes.AppFrameHomePage())["Home"],
                Ui.SidebarItem.Key("drivers").Icon(Ui.IconName.Identification).Href(Routes.AppFrameDriversPage())["Drivers"],
                Ui.SidebarGroup.Key("records").Expandable(true).Heading("Records").Icon(Ui.IconName.CircleStack).Expanded(false)[
                    Ui.SidebarItem.Key("vehicles").Icon(Ui.IconName.Truck).Href(Routes.AppFrameVehiclesPage())["Vehicles"]
                ],
                Ui.SidebarItem.Key("reports").Icon(Ui.IconName.ChartBar).Href("/old/reports")["Reports"]
            ],
            Outlet
        ];
}

/// <summary>Home: the row every other row is beneath.</summary>
[Route("")]
[ParentRoute(typeof(AppFrameLayout))]
public sealed partial class AppFrameHomePage : Component
{
    /// <inheritdoc />
    protected override Component? Render() => P.Id("page")["Home page"];
}

/// <summary>A list, with its editor over it and a way to one of its records.</summary>
[Route("drivers")]
[ParentRoute(typeof(AppFrameLayout))]
public sealed partial class AppFrameDriversPage : Component
{
    /// <inheritdoc />
    protected override string? PageTitle => "Drivers";

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Key("drivers")[
            P.Id("page")["Drivers list"],
            Button.Id("open-7").OnClick(() => Go.To("/ui-app-frame/drivers/7"))["Ada"],
            Ui.EditorDialog.In("edit")[P["Editor"]]
        ];
}

/// <summary>A page beneath the list: one driver.</summary>
[Route("drivers/{Id}")]
[ParentRoute(typeof(AppFrameLayout))]
public sealed partial class AppFrameDriverPage : Component
{
    [RouteParam] public string? Id { get; set; }

    /// <inheritdoc />
    protected override string? PageTitle => "Ada Lovelace";

    /// <inheritdoc />
    protected override Component? Render() => Div.Key("driver")[P.Id("page")["Driver " + Id]];
}

/// <summary>A list in a group of the menu.</summary>
[Route("vehicles")]
[ParentRoute(typeof(AppFrameLayout))]
public sealed partial class AppFrameVehiclesPage : Component
{
    /// <inheritdoc />
    protected override string? PageTitle => "Vehicles";

    /// <inheritdoc />
    protected override Component? Render() => P.Id("page")["Vehicles list"];
}

/// <summary>A page no row of the menu leads to.</summary>
[Route("settings")]
[ParentRoute(typeof(AppFrameLayout))]
public sealed partial class AppFrameSettingsPage : Component
{
    /// <inheritdoc />
    protected override string? PageTitle => "Settings";

    /// <inheritdoc />
    protected override Component? Render() => P.Id("page")["Settings"];
}

/// <summary><c>Ui.AppFrame</c>: the menu in Flux's sidebar, breadcrumbs from the menu, the user and sign-out, the page in the main.</summary>
public sealed partial class UiAppFrameTests
{
    [Fact]
    public void The_frame_draws_the_menu_in_the_sidebar_and_the_page_in_the_main()
    {
        var page = Page.Visit("/ui-app-frame/drivers");

        var sidebar = page.Find("[data-ui-sidebar]");
        var main = page.Find("[data-ui-main]");

        Assert.Contains("PPTA", page.TextOf("[data-ui-sidebar-brand]"), StringComparison.Ordinal);
        Assert.Equal("Acme Fuels", page.TextOf("[data-ui-app-frame-tenant]"));
        Assert.Contains("min-h-0 flex-1 overflow-y-auto", page.Find("[data-ui-sidebar-nav]").Attribute("class"), StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", page.TextOf("[data-ui-sidebar-profile]"), StringComparison.Ordinal);
        Assert.Contains("border-e", sidebar.Attribute("class"), StringComparison.Ordinal);
        Assert.Equal("Drivers list", page.TextOf("[data-ui-main] #page"));
        Assert.DoesNotContain(main.DescendantsAndSelf(), node => node.Attributes.ContainsKey("data-ui-sidebar-nav"));
        Assert.Equal("2 running", page.TextOf("[data-ui-header] #notices"));
    }

    [Fact]
    public void A_list_page_lights_its_row_and_ends_the_trail_in_its_title()
    {
        var page = Page.Visit("/ui-app-frame/drivers");

        var lit = page.FindAll("[data-ui-sidebar-nav] [aria-current=\"page\"]");
        var steps = page.FindAll("[data-ui-header] [data-ui-breadcrumbs-item]");

        Assert.Equal("Drivers", Assert.Single(lit).TextContent.Trim());
        Assert.Equal(2, steps.Count);
        Assert.Equal("/ui-app-frame", steps[0].DescendantsAndSelf().FirstOrDefault(node => node.Tag == "a")?.Attribute("href"));
        Assert.Null(steps[1].DescendantsAndSelf().FirstOrDefault(node => node.Tag == "a"));
        Assert.Equal("Drivers", steps[1].TextContent.Trim());
    }

    [Fact]
    public void A_page_beneath_a_list_keeps_its_row_lit_and_links_back_to_the_list()
    {
        var page = Page.Visit("/ui-app-frame/drivers/7");

        var lit = page.FindAll("[data-ui-sidebar-nav] [aria-current=\"page\"]");
        var steps = page.FindAll("[data-ui-header] [data-ui-breadcrumbs-item]");

        Assert.Equal("Drivers", Assert.Single(lit).TextContent.Trim());
        Assert.Equal(3, steps.Count);
        Assert.Equal("/ui-app-frame/drivers", steps[1].DescendantsAndSelf().FirstOrDefault(node => node.Tag == "a")?.Attribute("href"));
        Assert.Equal("Ada Lovelace", steps[2].TextContent.Trim());
        Assert.Equal("Driver 7", page.TextOf("#page"));
    }

    [Fact]
    public void A_phone_shows_the_last_two_steps_of_the_trail()
    {
        var page = Page.Visit("/ui-app-frame/drivers/7");

        var steps = page.FindAll("[data-ui-header] [data-ui-breadcrumbs-item]");

        Assert.Contains("max-sm:hidden", steps[0].Attribute("class"), StringComparison.Ordinal);
        Assert.DoesNotContain("max-sm:hidden", steps[1].Attribute("class"), StringComparison.Ordinal);
        Assert.DoesNotContain("max-sm:hidden", steps[2].Attribute("class"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_in_a_group_says_the_group_in_the_trail_and_opens_it()
    {
        var page = Page.Visit("/ui-app-frame/vehicles");

        var steps = page.FindAll("[data-ui-header] [data-ui-breadcrumbs-item]");
        var group = page.Find("[data-ui-sidebar-nav] details");

        Assert.Equal(["", "Records", "Vehicles"], steps.Select(step => step.TextContent.Trim()));
        Assert.Contains("max-sm:hidden", steps[0].Attribute("class"), StringComparison.Ordinal);
        Assert.NotNull(group.Attribute("open"));
        Assert.Equal("Vehicles", page.FindAll("[data-ui-sidebar-nav] [aria-current=\"page\"]")[0].TextContent.Trim());
    }

    [Fact]
    public void Home_lights_home_alone_and_its_trail_is_home()
    {
        var page = Page.Visit("/ui-app-frame");

        var lit = page.FindAll("[data-ui-sidebar-nav] [aria-current=\"page\"]");
        var steps = page.FindAll("[data-ui-header] [data-ui-breadcrumbs-item]");

        Assert.Equal("Home", Assert.Single(lit).TextContent.Trim());
        Assert.Single(steps);
        Assert.Equal("/ui-app-frame", page.Find("[data-ui-sidebar-brand]").Attribute("href"));
    }

    [Fact]
    public void A_page_no_row_leads_to_lights_nothing_and_its_trail_is_home()
    {
        var page = Page.Visit("/ui-app-frame/settings");

        var lit = page.FindAll("[data-ui-sidebar-nav] [aria-current=\"page\"]");
        var steps = page.FindAll("[data-ui-header] [data-ui-breadcrumbs-item]");

        Assert.Empty(lit);
        Assert.Single(steps);
        Assert.Equal("Settings", page.TextOf("#page"));
    }

    [Fact]
    public void A_link_out_of_the_app_is_never_lit()
    {
        var page = Page.Visit("/ui-app-frame/drivers");

        var reports = page.Find("[data-ui-sidebar-nav] a[href=\"/old/reports\"]");

        Assert.Null(reports.Attribute("aria-current"));
    }

    [Fact]
    public async Task The_way_back_to_a_list_keeps_its_filters_and_leaves_its_dialog_closed()
    {
        var page = Page.Visit("/ui-app-frame/drivers?q=ada&page=2&edit=new");

        await page.On("#open-7").Click();
        var back = page.FindAll("[data-ui-header] [data-ui-breadcrumbs-item]")[1].DescendantsAndSelf().FirstOrDefault(node => node.Tag == "a");

        Assert.Equal("Driver 7", page.TextOf("#page"));
        Assert.Equal("/ui-app-frame/drivers?q=ada&page=2", back?.Attribute("href"));
    }

    [Fact]
    public void The_profile_menu_signs_out_after_asking_in_the_catalogs_words()
    {
        var page = Page.Visit("/ui-app-frame/drivers");

        var row = page.Find("[data-ui-sidebar] a[href=\"/sign-out\"]");

        Assert.Equal("Sign out", row.TextContent.Trim());
        Assert.Contains("Are you sure you want to sign out?", row.Attributes.Values);
    }

    [Fact]
    public void The_frame_mounts_what_every_page_asks_in()
    {
        var page = Page.Visit("/ui-app-frame/drivers");

        var html = page.Html;

        Assert.Contains("rask-confirm-dialog", html, StringComparison.Ordinal);
        Assert.Contains("rask-leave", html, StringComparison.Ordinal);
        Assert.True(page.Exists("[data-ui-header] [data-ui-sidebar-toggle]"));
    }
}
