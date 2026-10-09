using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The documented save in a real browser: a guarded form whose <c>OnSubmit</c> awaits the save and then
///     goes to the list, which loads with two reads that pass no token.
/// </summary>
/// <remarks>
///     The list mounts inside the save handler's turn. It used to load under the saving page's lifetime, which
///     the navigation ends — so its second read was cancelled and the page it showed was an error.
/// </remarks>
public sealed class ArrivalAfterSaveTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_guarded_form_that_saves_and_goes_to_the_list_lands_on_a_list_with_both_reads()
    {
        await using var session = await HookSession.OpenAsync<ArrivalJourneyPage>(playwright);
        var page = session.Page;
        List<string> asked = [];
        page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Message);
            _ = dialog.DismissAsync();
        };

        await page.FillAsync("#name", "Lisbon");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#list")).ToHaveTextAsync("rows:3 total:3");
        Assert.EndsWith("/list", page.Url, StringComparison.Ordinal);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task A_list_reached_by_a_link_loads_the_same_way()
    {
        await using var session = await HookSession.OpenAsync<ArrivalJourneyPage>(playwright);
        var page = session.Page;

        await page.ClickAsync("#to-list");

        await Expect(page.Locator("#list")).ToHaveTextAsync("rows:3 total:3");
    }
}

internal sealed class ArrivalDestination
{
    public string Name { get; set; } = "";
}

/// <summary>A save and a read as a data call makes them: no token passed.</summary>
internal static class ArrivalJourneyData
{
    // Its own async method, whose continuation carries the handler's flow onto the pool.
    public static async Task Save() => await Task.Delay(20).ConfigureAwait(false);

    public static async Task<int> Read()
    {
        var token = Current.Cancellation;
        await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return 3;
    }
}

/// <summary>A save form at <c>/</c> and the list it goes to at <c>/list</c>.</summary>
public sealed partial class ArrivalJourneyPage(RouteState route) : Component
{
    protected override Component? HeadAssets => Markup.Title["arrival"];

    protected override string? HtmlLang => "en";

    protected override Task OnMount()
    {
        route.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        route.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        NavLink.Href("/list").Id("to-list")["list"],
        string.Equals(route.Path, "/list", StringComparison.Ordinal) ? ArrivalJourneyList : ArrivalJourneyForm
    ];
}

public sealed partial class ArrivalJourneyForm : Component
{
    private readonly ArrivalDestination _destination = new();

    protected override Component? Render() =>
        Form.Model(_destination).OnSubmit(Save).ConfirmLeave("Leave without saving?")[
            Input.Bind(() => _destination.Name).Id("name"),
            Button.Type(ButtonType.Submit).Id("save")["save"]
        ];

    private async Task Save(ArrivalDestination destination)
    {
        await ArrivalJourneyData.Save();
        Go.To("/list");
    }
}

public sealed partial class ArrivalJourneyList : Component
{
    private int? _rows;
    private int? _total;

    protected override async Task OnMount()
    {
        _rows = await ArrivalJourneyData.Read();
        _total = await ArrivalJourneyData.Read();
    }

    protected override Component? Render() =>
        P.Id("list")[_total is null ? "loading" : $"rows:{_rows} total:{_total}"];
}
