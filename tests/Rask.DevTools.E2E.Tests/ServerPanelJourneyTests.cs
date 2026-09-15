using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.DevTools.E2E.Tests.Apps;
using Rask.DevTools.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.DevTools.E2E.Tests;

/// <summary>
///     Opening the devtools on a Server page, and the Wire tab reading the page's own traffic: the parts every other tab
///     stands on.
/// </summary>
[Collection(DevToolsHostCollection.Name)]
public sealed partial class ServerPanelJourneyTests(PlaywrightFixture playwright)
{
    [Fact]
    public async Task The_pill_opens_the_panel_the_shortcut_closes_it_and_a_reload_keeps_the_side_it_was_docked_on()
    {
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await Expect(devTools.Drawer).ToBeHiddenAsync();

        await devTools.OpenPanelAsync();
        await Expect(devTools.Drawer).ToBeVisibleAsync(DevToolsPage.Visible);

        // Docked right, then shut with the shortcut from the page: the app never sees the keys.
        await devTools.Page.Locator("rask-devtools").GetByRole(AriaRole.Button, new() { Name = "Right" }).ClickAsync();
        await devTools.Page.Keyboard.PressAsync("Control+Shift+KeyD");
        await Expect(devTools.Drawer).ToBeHiddenAsync(new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds });

        await devTools.Page.ReloadAsync();
        await Expect(devTools.Pill).ToBeVisibleAsync(DevToolsPage.Visible);
        await devTools.Page.Keyboard.PressAsync("Control+Shift+KeyD");
        await Expect(devTools.Tab("Wire")).ToBeVisibleAsync(DevToolsPage.Visible);
        Assert.Equal("right", await devTools.Page.EvaluateAsync<string?>("() => localStorage.getItem('rask.devtools.dock')"));
    }

    [Fact]
    public async Task The_Wire_tab_counts_the_pages_clicks_and_frames_but_never_the_panels_own()
    {
        await using var host = await DevToolsServerHost.StartAsync<BoardApp>();
        await using var context = await playwright.Browser.NewContextAsync();
        var devTools = await DevToolsPage.OpenAsync(context, host.BaseUrl + "/");
        await devTools.OpenPanelAsync();

        await devTools.Page.ClickAsync("#add-task");
        await Expect(devTools.Page.Locator("#board li")).ToHaveCountAsync(3, new() { Timeout = (float)DevToolsPage.Wait.TotalMilliseconds });
        await Expect(devTools.Panel.GetByText(FramesSentOne()).First).ToBeVisibleAsync(DevToolsPage.Visible);
        await Expect(devTools.Panel.Locator("tbody tr", new() { HasText = "click" }).First).ToBeVisibleAsync(DevToolsPage.Visible);

        // The panel is a live page too: switching its tabs sends frames of its own, none of which is the app's.
        await devTools.ShowTabAsync("Tree");
        await devTools.ShowTabAsync("Renders");
        await devTools.ShowTabAsync("Wire");
        await Expect(devTools.Panel.GetByText(FramesSentOne()).First).ToBeVisibleAsync(DevToolsPage.Visible);
    }

    [GeneratedRegex(@"Frames sent\s*1\b")]
    private static partial Regex FramesSentOne();
}
