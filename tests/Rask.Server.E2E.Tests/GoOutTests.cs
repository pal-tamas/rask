using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Go.Out("/old/tenants")</c> in a real browser: a new app mapped under <c>/uj</c> sends the reader to a
///     page of the old application beside it, which no Rask app renders.
/// </summary>
/// <remarks>
///     The address is used as written — no <c>/uj</c> in front — and the browser loads it as a page. The app sent
///     the reader there, as a handler's <c>Go.To</c> does, so a guarded form holding unsaved edits does not ask.
/// </remarks>
[Collection(PathBaseCollection.Name)]
public sealed class GoOutTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_handler_that_goes_out_loads_the_page_outside_the_path_base_without_the_leave_guard_asking()
    {
        await using var session = await HookSession.OpenAsync<OutApp>(
            playwright, path: "/uj/out/form", pathBase: "/uj", beside: MapTheOldApplication);
        var page = session.Page;
        List<string> asked = [];
        page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ": " + dialog.Message);
            _ = dialog.DismissAsync();
        };
        await page.FillAsync("#name", "unsaved");

        await page.ClickAsync("#leave");

        await Expect(page.Locator("#old")).ToHaveTextAsync("old tenants");
        Assert.Equal(session.BaseUrl + "/old/tenants", page.Url);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task A_link_to_a_page_that_goes_out_as_it_mounts_loads_the_page_outside_the_path_base()
    {
        await using var session = await HookSession.OpenAsync<OutApp>(
            playwright, path: "/uj/out/form", pathBase: "/uj", beside: MapTheOldApplication);
        var page = session.Page;

        await page.ClickAsync("#to-unchosen");

        await Expect(page.Locator("#old")).ToHaveTextAsync("old tenants");
        Assert.Equal(session.BaseUrl + "/old/tenants", page.Url);
    }

    [Fact]
    public async Task Opening_a_page_that_goes_out_by_its_address_is_redirected_outside_the_path_base()
    {
        await using var session = await HookSession.OpenAsync<OutApp>(
            playwright, path: "/uj/out/unchosen", pathBase: "/uj", beside: MapTheOldApplication);
        var page = session.Page;

        var shown = await page.Locator("#old").InnerTextAsync();

        Assert.Equal("old tenants", shown);
        Assert.Equal(session.BaseUrl + "/old/tenants", page.Url);
    }

    private static void MapTheOldApplication(WebApplication app) =>
        app.MapGet(
            "/old/tenants",
            () => Results.Content("<!DOCTYPE html><html><head><title>Old</title></head><body><p id=\"old\">old tenants</p></body></html>", "text/html"));
}

internal sealed class OutDraft
{
    public string Name { get; set; } = "";
}

/// <summary>A new app that lives under a path base, beside an old one it sometimes sends the reader to.</summary>
public sealed partial class OutApp : Component
{
    protected override Component? Render() => Router;
}

[Route("/out")]
public sealed partial class OutLayout : Component
{
    // The runtime reads the path base off the document, as a browser does.
    protected override Component? HeadAssets => [Title["New"], Base.Href("/uj/")];

    protected override Component? Render() =>
    [
        NavLink.Href(Routes.OutUnchosenPage()).Id("to-unchosen")["Work"],
        Main[Outlet],
    ];
}

[Route("form")]
[ParentRoute(typeof(OutLayout))]
public sealed partial class OutFormPage : Component
{
    private readonly OutDraft _draft = new();

    protected override Component? Render() =>
        Form.Model(_draft).OnSubmit(_ => Task.CompletedTask).ConfirmLeave("Leave without saving?")[
            Input.Bind(() => _draft.Name).Id("name"),
            Button.Type(ButtonType.Button).Id("leave").OnClick(() => Go.Out("/old/tenants"))["Tenants (old)"]
        ];
}

// No partner is chosen here, and the page where one is chosen belongs to the old application.
[Route("unchosen")]
[ParentRoute(typeof(OutLayout))]
public sealed partial class OutUnchosenPage : Component
{
    protected override Task OnMount()
    {
        Go.Out("/old/tenants");
        return Task.CompletedTask;
    }

    protected override Component? Render() => P.Id("unchosen")["work for nobody"];
}

/// <summary>
///     The suites whose app is mapped under a path base. The path base is one value for the whole process, so such a
///     host cannot run beside one that has none: every page of the other would be addressed under it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PathBaseCollection
{
    public const string Name = "PathBase";
}
