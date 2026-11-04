using Rask.Core;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     The browser's own select (Ui.Select with no variant) takes only a value one of the options it drew sends
///     (#1268) — the rule the held list already keeps. An id the reader was never offered leaves the model as it was.
/// </summary>
public sealed partial class NativeSelectOffersTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_bound_native_select_refuses_an_id_none_of_its_options_offered()
    {
        var order = new Order { CompanyId = 5 };
        using var session = new OlderPage(Companies(order));

        await session.TypedAsync(session.Id("change"), "999");

        Assert.Equal(5, order.CompanyId);
    }

    [Fact]
    public async Task A_bound_native_select_takes_an_id_one_of_its_options_offered()
    {
        var order = new Order { CompanyId = 5 };
        using var session = new OlderPage(Companies(order));

        await session.TypedAsync(session.Id("change"), "10");

        Assert.Equal(10, order.CompanyId);
    }

    [Fact]
    public async Task A_bound_native_select_refuses_the_id_of_a_disabled_option()
    {
        var order = new Order { CompanyId = 5 };
        using var session = new OlderPage(Companies(order));

        await session.TypedAsync(session.Id("change"), "7");

        Assert.Equal(5, order.CompanyId);
    }

    [Fact]
    public async Task A_native_select_refuses_its_placeholder_which_the_browser_never_sends()
    {
        var order = new Order { CompanyId = 5 };
        using var session = new OlderPage(new StubComponent(() =>
            Ui.Select.Bind(() => order.CompanyId).Placeholder("Choose a company")[Ui.SelectOption.Value(5)["Acme"]]));

        await session.TypedAsync(session.Id("change"), "");

        Assert.Equal(5, order.CompanyId);
    }

    [Fact]
    public async Task A_bound_native_select_picked_several_pages_late_still_lands()
    {
        var order = new Order { CompanyId = 5 };
        using var session = new OlderPage(Companies(order));
        var id = session.Id("change");

        await session.TypedAsync(id, "10");
        await session.TypedAsync(id, "11");
        var heard = await session.TypedAsync(id, "5");

        Assert.True(heard);
        Assert.Equal(5, order.CompanyId);
    }

    [Fact]
    public async Task A_controlled_native_select_does_not_hear_an_id_none_of_its_options_offered()
    {
        var heard = new List<int>();
        using var session = new OlderPage(new StubComponent(() =>
            Ui.Select.Value(5).OnChange(heard.Add)[Ui.SelectOption.Value(5)["Acme"], Ui.SelectOption.Value(10)["Globex"]]));

        await session.TypedAsync(session.Id("change"), "999");

        Assert.Empty(heard);
    }

    private static StubComponent Companies(Order order) =>
        new(() => Ui.Select.Bind(() => order.CompanyId)[
            Ui.SelectOption.Value(5)["Acme"],
            Ui.SelectOption.Value(7).Disabled()["Initech (closed)"],
            Ui.SelectOption.Value(10)["Globex"],
            Ui.SelectOption.Value(11)["Umbrella"]
        ]);

    private sealed class Order
    {
        public int? CompanyId { get; set; }
    }
}
