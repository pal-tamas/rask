using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A list filtered, sorted and paged in its address, showing where it stands, and a way to one of its records.</summary>
[Route("/ui-list-routes/parcels")]
public sealed partial class ListRoutesParcelsPage(RouteState route) : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Key("parcels")[
            P.Id("at")[string.Join('&', route.Query.Select(q => q.Key + "=" + q.Value))],
            Button.Id("open-7").OnClick(() => Go.To("/ui-list-routes/parcels/7"))["Parcel 7"],
            Ui.Table.SortIn("dir").PageIn("page", 30, perPage: 10)[
                Ui.TableColumns[Ui.TableColumn.Sortable()["Name"]],
                Ui.TableRows[Ui.TableRow.Key("seven")[Ui.TableCell["Parcel 7"]]]
            ]
        ];
}

/// <summary>A page of its own for one parcel: a Cancel link and a save, each back to the list as it was left.</summary>
[Route("/ui-list-routes/parcels/{Id}")]
public sealed partial class ListRoutesParcelPage : Component
{
    [RouteParam] public string? Id { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Key("parcel")[
            Ui.Button.Key("cancel").Ghost.Href(Routes.ListRoutesParcelsPage().AsLeft())["Cancel"],
            Button.Id("save").OnClick(() => Routes.ListRoutesParcelsPage().AsLeft().Go().Replacing())["Save"]
        ];
}

/// <summary><see cref="UiListRoutes.AsLeft" />: the way back to a list, as the reader left it, for the app's own code.</summary>
public partial class UiListRoutesTests : RaskMarkup
{
    [Fact]
    public async Task A_link_back_to_the_list_keeps_its_filters_sort_and_page()
    {
        var page = Page.Visit("/ui-list-routes/parcels?q=north&dir=desc&page=2");

        await page.On("#open-7").Click();
        var cancel = page.Find("a[data-rask-key=\"cancel\"]");

        Assert.Equal("/ui-list-routes/parcels?q=north&dir=desc&page=2", cancel.Attribute("href"));
    }

    [Fact]
    public async Task A_save_goes_back_to_the_list_as_it_was_left()
    {
        var page = Page.Visit("/ui-list-routes/parcels?q=north&dir=desc&page=2");
        await page.On("#open-7").Click();

        await page.On("#save").Click();

        Assert.Equal("q=north&dir=desc&page=2", page.TextOf("#at"));
    }

    [Fact]
    public void A_list_this_session_has_not_shown_is_the_route_as_given()
    {
        var page = Page.Visit("/ui-list-routes/parcels/7");

        var cancel = page.Find("a[data-rask-key=\"cancel\"]");

        Assert.Equal("/ui-list-routes/parcels", cancel.Attribute("href"));
    }

    [Fact]
    public void Outside_any_session_the_route_is_as_given()
    {
        var list = new RouteUrl("/parcels");

        var back = list.AsLeft();

        Assert.Equal("/parcels", back.ToString());
    }
}
