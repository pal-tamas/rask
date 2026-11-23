using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.UiTests.Components;

/// <summary>Where each list was last shown in a session, its query kept: the way the frame's breadcrumbs lead back.</summary>
public sealed class UiListAddressesTests
{
    [Fact]
    public void A_list_goes_back_as_it_was_last_filtered_sorted_and_paged()
    {
        var route = new RouteState();
        var lists = UiListAddresses.Of(route);

        Move(route, "/invoices", ("sort", "issued"), ("dir", "desc"), ("page", "3"));
        Move(route, "/invoices/57265");
        var back = lists.Back(new RouteUrl("/invoices"));

        Assert.Equal("/invoices?sort=issued&dir=desc&page=3", back.ToString());
    }

    [Fact]
    public void A_list_never_shown_goes_back_bare()
    {
        var route = new RouteState();
        var lists = UiListAddresses.Of(route);

        Move(route, "/vehicles", ("plate", "ABC"));
        var back = lists.Back(new RouteUrl("/invoices"));

        Assert.Equal("/invoices", back.ToString());
    }

    [Fact]
    public void A_list_whose_filters_were_cleared_goes_back_bare()
    {
        var route = new RouteState();
        var lists = UiListAddresses.Of(route);

        Move(route, "/invoices", ("paid", "false"));
        Move(route, "/invoices");
        Move(route, "/invoices/1");
        var back = lists.Back(new RouteUrl("/invoices"));

        Assert.Equal("/invoices", back.ToString());
    }

    [Fact]
    public void The_address_the_session_opened_on_counts_as_shown()
    {
        var route = new RouteState();
        Move(route, "/invoices", ("number", "SZ-1"));

        var lists = UiListAddresses.Of(route);
        Move(route, "/invoices/1");

        Assert.Equal("/invoices?number=SZ-1", lists.Back(new RouteUrl("/invoices")).ToString());
    }

    [Fact]
    public void A_list_is_remembered_whatever_its_trailing_slash()
    {
        var route = new RouteState();
        var lists = UiListAddresses.Of(route);

        Move(route, "/invoices/", ("paid", "true"));
        Move(route, "/invoices/1");
        var back = lists.Back(new RouteUrl("/invoices"));

        Assert.Equal("/invoices?paid=true", back.ToString());
    }

    [Fact]
    public void A_list_is_remembered_whatever_the_case_of_its_address()
    {
        var route = new RouteState();
        var lists = UiListAddresses.Of(route);

        Move(route, "/Invoices", ("paid", "true"));
        Move(route, "/invoices/1");
        var back = lists.Back(new RouteUrl("/invoices"));

        Assert.Equal("/invoices?paid=true", back.ToString());
    }

    // As the navigator moves: the path first, then the query.
    private static void Move(RouteState route, string path, params (string Key, string Value)[] query)
    {
        route.Path = path;
        route.Query = new QueryCollection(query.ToDictionary(pair => pair.Key, pair => new StringValues(pair.Value)));
    }
}
