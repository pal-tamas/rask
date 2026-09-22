using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Layout and Mockup components, in a browser.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitLayoutTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_layout_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-drawer", "ui-layout-rest", "ui-layout-mask", "ui-mockups",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task The_Flux_layout_pieces_work() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-app-layout']");

        // The item for this page is current without being told, and says so to assistive tech.
        await Expect(scope.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Layout" }))
            .ToHaveAttributeAsync("aria-current", "page", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });
        await Expect(scope.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Actions 5" }))
            .Not.ToHaveAttributeAsync("aria-current", "page");

        // The spacer pushes "Sign in" to the far end of its row.
        var row = await Page.Locator("[data-testid='ui-spacer-row']").BoundingBoxAsync();
        var signIn = await Page.Locator("[data-testid='ui-spacer-row']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Sign in" }).BoundingBoxAsync();
        Assert.True(row!.X + row.Width - (signIn!.X + signIn.Width) < 16, "the spacer did not push the last button to the end.");

        // The separator has no margin of its own — daisyUI's 1rem is zeroed.
        var margin = await scope.Locator(".divider").Last.EvaluateAsync<string>("d => getComputedStyle(d).marginTop");
        Assert.Equal("0px", margin);
    });

    [Fact]
    public Task The_docs_sidebar_is_the_kits_sidebar_and_opens_from_the_keyboard_on_a_phone() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.SetViewportSizeAsync(390, 844);
        try
        {
            var sideNav = Page.Locator(".side-nav");
            await Expect(sideNav).Not.ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions { Timeout = 10_000 });

            // The hamburger is a label for the sidebar's checkbox: a keyboard stop the runtime presses on Enter.
            await Page.Locator(".hamburger-btn").FocusAsync();
            await Page.Keyboard.PressAsync("Enter");

            await Expect(sideNav).ToBeInViewportAsync(new LocatorAssertionsToBeInViewportOptions { Timeout = 10_000 });
            await Expect(Page.Locator("aside.side-nav[aria-label='Guides and examples']")).ToHaveCountAsync(1);
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task The_drawer_opens_and_the_page_is_told_about_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-drawer']");
        var state = Page.Locator("[data-testid='ui-drawer-state']");

        await Expect(state).ToContainTextAsync("closed");

        await scope.GetByText("Open the drawer").ClickAsync();

        // Both halves. The panel slides because the checkbox is checked — that is daisyUI's CSS, not
        // the kit's — and the PAGE knows, which is what the checkbox alone could never report.
        await Expect(state).ToContainTextAsync("open");
        await Expect(scope.GetByText("Queues")).ToBeVisibleAsync();
    });

    [Fact]
    public Task A_checked_control_is_actually_checked_in_the_DOM() => RunAsync(async () =>
    {
        // The regression this guards is the one markup could not show: the kit wrote the checked state
        // into the VALUE attribute, so a control the page said was on arrived off. Asserted through the
        // DOM's own property rather than the attribute, which is what a reader actually sees.
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await ClickSidebar("Data input");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data input",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var remember = Page.Locator("[data-testid='ui-choices'] input.checkbox").First;
        await Expect(remember).ToBeCheckedAsync();

        var alerts = Page.Locator("[data-testid='ui-choices'] input.toggle").First;
        await Expect(alerts).Not.ToBeCheckedAsync();
    });

    [Fact]
    public Task The_code_mockup_shows_its_prefixes_and_its_text() => RunAsync(async () =>
    {
        await OpenAsync();

        var code = Page.Locator("[data-testid='ui-mockups'] .mockup-code");

        await Expect(code).ToBeVisibleAsync();
        await Expect(code).ToContainTextAsync("rask new shop");
        await Expect(code.Locator("pre[data-prefix='$']")).ToHaveCountAsync(2);
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Layout & mockups");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Layout",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
