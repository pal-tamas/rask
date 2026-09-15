using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.DevTools.E2E.Tests.Infrastructure;

/// <summary>A page with the devtools on it, as a developer meets them: the pill, the drawer, the panel inside it.</summary>
internal sealed class DevToolsPage(IPage page)
{
    /// <summary>How long a step waits for what it expects to see. Generous: a panel boots a page of its own.</summary>
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    internal static readonly LocatorAssertionsToBeVisibleOptions Visible = new() { Timeout = (float)Wait.TotalMilliseconds };

    internal static readonly LocatorAssertionsToHaveTextOptions Text = new() { Timeout = (float)Wait.TotalMilliseconds };

    internal static readonly LocatorAssertionsToContainTextOptions Contains = new() { Timeout = (float)Wait.TotalMilliseconds };

    internal IPage Page { get; } = page;

    /// <summary>The pill, inside the devtools' own shadow root (CSS selectors pierce an open one).</summary>
    internal ILocator Pill => Page.Locator("rask-devtools .pill");

    internal ILocator Drawer => Page.Locator("rask-devtools .drawer");

    /// <summary>The panel: a Rask page of its own, in the drawer's frame.</summary>
    internal IFrameLocator Panel => Page.FrameLocator("rask-devtools iframe");

    internal static async Task<DevToolsPage> OpenAsync(IBrowserContext context, string url)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(url);
        var devTools = new DevToolsPage(page);
        await Expect(devTools.Pill).ToBeVisibleAsync(Visible);
        return devTools;
    }

    /// <summary>Opens the drawer from the pill and waits until the panel has drawn its tabs.</summary>
    internal async Task OpenPanelAsync()
    {
        await Pill.ClickAsync();
        await Expect(Tab("Wire")).ToBeVisibleAsync(Visible);
    }

    // By the name's first word: a tab with news carries its count too, "Errors 1".
    internal ILocator Tab(string name) =>
        Panel.GetByRole(AriaRole.Tab, new() { NameRegex = new System.Text.RegularExpressions.Regex("^" + name + @"\b") });

    /// <summary>Picks a tab and waits until the panel says it is the selected one.</summary>
    internal async Task ShowTabAsync(string name)
    {
        await Tab(name).ClickAsync();
        await Expect(Panel.Locator("[role=tab][aria-selected=\"true\"]")).ToContainTextAsync(name, Contains);
    }
}
