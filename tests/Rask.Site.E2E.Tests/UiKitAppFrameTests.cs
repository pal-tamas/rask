using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>Ui.AppFrame</c> in a browser (#1296): the app frame page frames its demo app, the menu lights the list beneath a
///     record, the trail leads back to the list as it was left, and a phone slides the menu in and keeps two steps.
/// </summary>
public sealed partial class UiKitAppFrameTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task The_app_frame_page_frames_its_demo_app_drawn_whole() => RunAsync(async () =>
    {
        await OpenUiPageAsync("app-frame", "App frame");
        var example = Page.Locator("[data-testid='ui-app-frame']");
        await example.ScrollIntoViewIfNeededAsync();

        var frame = example.FrameLocator("iframe");

        await Expect(frame.Locator("[data-ui-main] h1")).ToHaveTextAsync("Good afternoon, Mara", new() { Timeout = 30_000 });
        await Expect(frame.Locator("[data-ui-sidebar-nav] [aria-current='page']").First).ToHaveTextAsync("Home");
        await Expect(frame.Locator("[data-ui-sidebar-profile]")).ToContainTextAsync("Mara Lind");
    });

    [Fact]
    public Task A_record_keeps_its_list_lit_and_the_trail_goes_back_to_the_list_as_it_was_left() => RunAsync(async () =>
    {
        await Page.GotoAsync($"{BaseUrl}/demo/app-frame/");
        await WaitForInteractiveAsync();
        await Page.Locator("[data-ui-sidebar-nav] a", new() { HasText = "Drivers" }).ClickAsync();
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Active" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(ActiveDrivers(), new() { Timeout = 15_000 });

        await Page.Locator("[data-testid='af-driver-ada']").ClickAsync();

        var trail = Page.Locator("[data-ui-header] [data-ui-breadcrumbs-item]");
        await Expect(trail.Last).ToHaveTextAsync("Ada Lovelace", new() { Timeout = 15_000 });
        await Expect(Page.Locator("[data-ui-sidebar-nav] [aria-current='page']").First).ToHaveTextAsync("Drivers");
        await trail.Nth(1).GetByRole(AriaRole.Link, new() { Name = "Drivers" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(ActiveDrivers(), new() { Timeout = 15_000 });
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Active" })).ToHaveAttributeAsync("aria-selected", "true");
    });

    [Fact]
    public Task A_row_in_a_group_names_the_group_in_the_trail() => RunAsync(async () =>
    {
        await Page.GotoAsync($"{BaseUrl}/demo/app-frame/vehicles/");
        await WaitForInteractiveAsync();

        var trail = Page.Locator("[data-ui-header] [data-ui-breadcrumbs-item]");

        await Expect(trail).ToHaveCountAsync(3, new() { Timeout = 15_000 });
        await Expect(trail.Nth(1)).ToHaveTextAsync("Fleet");
        await Expect(trail.Nth(2)).ToHaveTextAsync("Vehicles");
    });

    [Fact]
    public Task On_a_phone_the_header_slides_the_menu_in_and_the_trail_keeps_two_steps() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{BaseUrl}/demo/app-frame/vehicles/");
        await WaitForInteractiveAsync();
        var sidebar = Page.Locator("[data-ui-sidebar]");
        await Expect(sidebar).Not.ToBeInViewportAsync(new() { Timeout = 15_000 });

        await Page.Locator("[data-ui-header] [data-ui-sidebar-toggle]").ClickAsync();

        await Expect(sidebar).ToBeInViewportAsync(new() { Timeout = 15_000 });
        var trail = Page.Locator("[data-ui-header] [data-ui-breadcrumbs-item]");
        await Expect(trail.Nth(0)).ToBeHiddenAsync();
        await Expect(trail.Nth(1)).ToBeVisibleAsync();
        await Expect(trail.Nth(2)).ToBeVisibleAsync();
    });

    [Fact]
    public Task The_sign_out_row_asks_before_it_goes() => RunAsync(async () =>
    {
        await Page.GotoAsync($"{BaseUrl}/demo/app-frame/");
        await WaitForInteractiveAsync();
        await Page.Locator("[data-ui-sidebar-profile]").ClickAsync();

        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Sign out" }).ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync("Are you sure you want to sign out?", new() { Timeout = 15_000 });
        await Expect(Page).ToHaveURLAsync(new Regex("/demo/app-frame/$"));
    });

    [GeneratedRegex("/demo/app-frame/drivers/?\\?status=active$")]
    private static partial Regex ActiveDrivers();
}
