using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     THE KEYBOARD RING (owner, 2026-11-10) on the showcase: Tab through every control of the kit's pages, on a desktop
///     and a phone, light and dark, and every ring is the app's accent and none of it is cut by a box around it — TA's ⋯
///     in a table's sticky last cell drew Safari's blue ring, cut by the table's scroll frame.
/// </summary>
public sealed class UiKitKeyboardRingTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    // Every page whose controls take the keyboard: buttons, links, tabs, boxes, fields and triggers, a table with its
    // row menus, the nav lists and the app frame.
    private static readonly (string Slug, string Heading)[] Pages =
    [
        ("button", "Button"), ("tabs", "Tabs"), ("checkbox", "Checkbox"), ("radio", "Radio"), ("switch", "Switch"),
        ("select", "Select"), ("pillbox", "Pillbox"), ("table", "Table"), ("row-actions", "Row actions"),
        ("navlist", "Navlist"), ("navbar", "Navbar"), ("dropdown", "Dropdown"), ("pagination", "Pagination"),
        ("date-picker", "Date picker"), ("app-frame", "App frame"), ("input", "Input"), ("theming", "Theming"),
        ("color-picker", "Color picker"), ("slider", "Slider"), ("toggle", "Toggle"),
    ];

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData(1280, false)]
    [InlineData(1280, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public Task Every_control_the_keyboard_reaches_is_ringed_in_the_accent_and_nothing_cuts_the_ring(int width, bool dark) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, width > 400 ? 900 : 844);
        var faults = new List<string>();
        var walked = 0;

        foreach (var (slug, heading) in Pages)
        {
            await OpenUiPageAsync(slug, heading);
            await KeyboardRing.SchemeAsync(Page, dark);
            var (found, stops) = await KeyboardRing.SweepAsync(Page);
            faults.AddRange(found.Select(f => $"{slug}: {f}"));
            walked += stops;
        }

        Assert.True(walked > 100, $"only {walked} stops were walked");
        Assert.True(faults.Count == 0, string.Join('\n', faults));
    });
}
