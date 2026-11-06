#:package Microsoft.Playwright
#:property PublishAot=false
// Drives the real devtools against tests/Rask.DevTools.Showcase and saves the guide's screenshots as PNG.
// Run through scripts/capture-devtools-screenshots.sh, which starts the app and converts the files to WebP.
//
//   dotnet run scripts/capture-devtools-screenshots.cs -- <app url> <out dir>
//
// A .NET 10 file-based app on the repo's own Microsoft.Playwright (its version is Directory.Packages.props'), with
// the Chromium that package installs: no Node, no npm, no csproj.
//
// Every step waits on what the reader of the guide will see — the pill, a tab, a row — rather than on time, and
// fails loudly when it does not appear: a screenshot of a panel that never loaded is worse than none. A failed run
// leaves failure.png beside the others, which the shell script keeps when RASK_CAPTURE_KEEP names a folder.
using System.Text.RegularExpressions;
using Microsoft.Playwright;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: dotnet run scripts/capture-devtools-screenshots.cs -- <app url> <out dir>");
    return 2;
}

var (url, outDir) = (args[0], args[1]);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync();
var context = await browser.NewContextAsync(new()
{
    ViewportSize = new() { Width = 1280, Height = 1100 },
    DeviceScaleFactor = 2,
    ColorScheme = ColorScheme.Light,
});
var page = await context.NewPageAsync();
Task Shot(string name) => page.ScreenshotAsync(new() { Path = Path.Combine(outDir, name + ".png") });
LocatorWaitForOptions Within(float ms) => new() { Timeout = ms };
PageWaitForFunctionOptions Polled(float ms) => new() { Timeout = ms };

try
{
    // The pill alone, on a window no taller than the page, so it is not a speck under a screen of nothing.
    await page.SetViewportSizeAsync(1280, 640);
    await page.GotoAsync(url);
    var pill = page.Locator("rask-devtools .pill");
    await pill.WaitForAsync(Within(30000));
    await Shot("pill");
    await page.SetViewportSizeAsync(1280, 1100);

    // Open the drawer at the bottom, and give the Wire tab something to show: two clicks in the app.
    await page.EvaluateAsync("() => localStorage.setItem('rask.devtools.dock', 'bottom')");
    await pill.ClickAsync();
    var panel = page.FrameLocator("rask-devtools iframe");
    await panel.GetByText(new Regex("Frames received|No traffic yet")).First.WaitForAsync(Within(30000));
    var addTask = page.GetByRole(AriaRole.Button, new() { Name = "Add task" });
    await addTask.ClickAsync();
    await panel.GetByText(new Regex(@"Frames sent\s*1\b")).First.WaitForAsync(Within(10000));
    await addTask.ClickAsync();
    await panel.GetByText(new Regex(@"Frames sent\s*2\b")).First.WaitForAsync(Within(10000));
    await Shot("wire");

    // The Tree tab, opened down to the board's rows on its own. Type-ahead moves the cursor to the deploy card and
    // scrolls it into view: its row is the one with the redacted token.
    await panel.GetByRole(AriaRole.Tab, new() { Name = "Tree" }).ClickAsync();
    var tree = panel.GetByRole(AriaRole.Tree);
    await tree.WaitForAsync(Within(10000));
    var row = tree.Locator("[role=\"treeitem\"]", new() { HasText = "TaskRow" }).First;
    await row.WaitForAsync(Within(10000));
    await row.Locator(".rask-devtools-row").First.ClickAsync();
    await page.Keyboard.TypeAsync("DeployCard");
    await panel.GetByText("ApiToken=••••").First.WaitForAsync(Within(10000));
    // The selected row's detail pane, down to the release it read from the board.
    await panel.Locator("[data-rask-devtools-reads]").GetByText("TaskBoard").WaitForAsync(Within(10000));
    await ScrollTabsIntoView();
    await Shot("tree");

    // Pointing at a row: the page shows the box around what that component rendered, with its label.
    var taskRow = tree.Locator("[role=\"treeitem\"]", new() { HasText = "Tag the release" }).First;
    await taskRow.Locator(".rask-devtools-row").HoverAsync();
    await page.WaitForFunctionAsync("""
        () => {
            const root = document.querySelector("rask-devtools")?.shadowRoot;
            const label = root?.querySelector(".hl-label");
            return !!label && !label.hidden && label.textContent.startsWith("TaskRow");
        }
        """, null, Polled(10000));
    await Shot("highlight");

    // The Renders tab, counting what the two clicks above rendered.
    await page.Mouse.MoveAsync(0, 0);
    await panel.GetByRole(AriaRole.Tab, new() { Name = "Renders" }).ClickAsync();
    // Waited on what only this tab shows: the Tree tab names TaskBoard too, and a shot taken then catches the switch.
    await SelectedTab("Renders");
    await panel.GetByRole(AriaRole.Cell, new() { Name = "TaskBoard" }).First.WaitForAsync(Within(10000));
    await PointerOffTheRows();
    await ScrollTabsIntoView();
    await Shot("renders");

    // The Perf tab, after one more task: the click's handler, render, diff, size and the page's patch time.
    await panel.GetByRole(AriaRole.Tab, new() { Name = "Perf" }).ClickAsync();
    await SelectedTab("Perf");
    await addTask.ClickAsync();
    await page.Mouse.MoveAsync(0, 0);
    // All three clicks listed, once the page's patch time has arrived for each.
    var clickRows = panel.Locator("tbody tr", new() { HasText = "click" });
    await clickRows.Nth(2).WaitForAsync(Within(10000));
    await clickRows.Filter(new() { HasText = "…" }).First.WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10000 });
    await ScrollTabsIntoView();
    await Shot("perf");
    await panel.GetByRole(AriaRole.Tab, new() { Name = "Renders" }).ClickAsync();
    await SelectedTab("Renders");

    // Flashing: switched on in the Renders tab, then one more task added. Shot while both colours are on the page —
    // they fade within a second — with the pointer off the page so no hover box joins them.
    // A Ui.Switch: its input is screen-reader-only, so it is switched the way a person does it, by its label.
    var flash = panel.Locator("label", new() { HasText = "Flash on the page" });
    await flash.ClickAsync();
    await page.WaitForFunctionAsync("() => localStorage.getItem('rask.devtools.flash') === 'on'", null, Polled(10000));
    await page.WaitForTimeoutAsync(500);
    await addTask.ClickAsync();
    await page.Mouse.MoveAsync(0, 0);
    // Polled every animation frame (the default without an interval): the boxes are gone within a second.
    await page.WaitForFunctionAsync("""
        () => {
            const root = document.querySelector("rask-devtools")?.shadowRoot;
            const shown = selector => [...(root?.querySelectorAll(selector) ?? [])].filter(b => !b.hidden).length;
            return shown(".fl-render") >= 3 && shown(".fl-dom") >= 1;
        }
        """, null, Polled(10000));
    await Shot("flash");

    // Errors. Flashing off again, the drawer shut, then a deploy that throws: the runtime's dev-error overlay shows
    // it, with the devtools' "Open in DevTools" button, and the pill counts it.
    await flash.ClickAsync();
    await page.WaitForFunctionAsync("() => localStorage.getItem('rask.devtools.flash') === null", null, Polled(10000));
    await page.Keyboard.PressAsync("Control+Shift+D");
    await page.Locator("rask-devtools .drawer").WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10000 });
    await page.GetByRole(AriaRole.Button, new() { Name = "Deploy" }).ClickAsync();
    var openInDevTools = page.Locator("[data-rask-dev-error] [data-rask-devtools-open]");
    await openInDevTools.WaitForAsync(Within(10000));
    await page.Locator("rask-devtools .dot").Filter(new() { HasText = "1" }).WaitForAsync(Within(10000));
    await page.Mouse.MoveAsync(0, 0);
    await page.WaitForTimeoutAsync(1100); // the last flash boxes fade
    await Shot("error-overlay");

    // The overlay's button opens the drawer on the Errors tab, with the fault, where it happened, and its stack.
    await openInDevTools.ClickAsync();
    await SelectedTab("Errors");
    await panel.GetByText("locked by another deploy").First.WaitForAsync(Within(10000));
    // The tab clicked last still has focus, which draws it like the selected one.
    await panel.Locator("body").EvaluateAsync("() => (document.activeElement instanceof HTMLElement) && document.activeElement.blur()");
    await PointerOffTheRows();
    await ScrollTabsIntoView();
    await Shot("errors");
}
catch
{
    try
    {
        await Shot("failure");
    }
    catch (PlaywrightException)
    {
        // The page is gone with the failure; the exception below says why.
    }

    throw;
}

return 0;

Task ScrollTabsIntoView() => page.FrameLocator("rask-devtools iframe").Locator("[role=tablist]")
    .EvaluateAsync("el => el.scrollIntoView({block: 'start'})");

Task SelectedTab(string name) => page.FrameLocator("rask-devtools iframe")
    .Locator("[role=tab][aria-selected=\"true\"]", new() { HasText = name }).WaitForAsync(Within(10000));

Task PointerOffTheRows() => page.FrameLocator("rask-devtools iframe").Locator("body")
    .HoverAsync(new() { Position = new() { X = 5, Y = 5 } });
