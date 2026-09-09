using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Site.Tests.Infrastructure;
using Rask.Testing;
using Rask.Ui;

namespace Rask.Site.Tests;

/// <summary>
///     The theme picker reports a choice, stores it, and comes back knowing which one is showing.
/// </summary>
/// <remarks>
///     What replaced it matters to what is asserted here. The header used to carry
///     <c>UiThemeDropdown</c> — daisyUI's CSS-only <c>theme-controller</c> radios, which the stylesheet
///     matches with no script at all. Free, and unable to remember anything: nothing to write a choice
///     with, and an input that renders <c>checked=false</c> on every pass, so the next render dropped the
///     selection. On this site the next render is a navigation or the WASM first frame, so the theme
///     reset about as fast as it could be picked. The two properties below are exactly the two that were
///     missing: the choice LEAVES the component (to the boot script, which owns <c>&lt;html&gt;</c> and
///     <c>localStorage</c>), and a stored choice COMES BACK into it.
/// </remarks>
public sealed partial class ThemeMenuTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Renders_every_theme_and_marks_light_by_default()
    {
        var page = RaskTest.Render(() => ThemeMenu, TestServices.Default());

        // The whole set, one control each — the list is the kit's, not a copy kept here.
        Assert.Equal(UiTheme.All.Count, page.FindAll("button[aria-pressed]").Count);
        Assert.Contains("dracula", page.Html);
        Assert.Contains("cupcake", page.Html);

        // Light before anything is chosen, and exactly one control says so.
        var active = page.FindAll("button[aria-pressed=\"true\"]");
        Assert.Single(active);
        Assert.Contains("light", active[0].TextContent);
    }

    [Fact]
    public async Task Choosing_a_theme_hands_it_to_the_script_that_stores_it()
    {
        var js = new FakeJsRuntime();
        var services = TestServices.Default(js: js);
        var page = RaskTest.Render(() => ThemeMenu, services);

        await page.On("li[data-rask-key=\"dracula\"] button").ClickAsync();

        // raskSetTheme is the seam: it writes localStorage and stamps data-theme on <html>. The
        // component deliberately does NOT render the attribute itself — <html> belongs to the boot
        // script, because that script is the only code that runs before the first paint and so the only
        // place a saved theme can be applied without a flash of the default.
        var calls = js.GetCalls("raskSetTheme");
        Assert.Single(calls);
        Assert.Equal(UiTheme.Value(UiThemeName.Dracula), Assert.IsType<string>(calls[0]![0]));

        // …and the control it moved to is the one now marked.
        var active = page.FindAll("button[aria-pressed=\"true\"]");
        Assert.Single(active);
        Assert.Contains(UiTheme.Value(UiThemeName.Dracula), active[0].TextContent);
    }

    [Fact]
    public async Task A_stored_theme_is_read_back_and_marked()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("raskTheme", UiTheme.Value(UiThemeName.Dracula));
        var page = RaskTest.Render(() => ThemeMenu, TestServices.Default(js: js));

        var html = await page.WaitForAsync(h => h.Contains("aria-pressed=\"true\"", StringComparison.Ordinal)
                                                && h.Contains("dracula", StringComparison.Ordinal));

        Assert.True(js.CallCount("raskTheme") > 0, "the picker never asked the script what is stored.");
        var active = page.FindAll("button[aria-pressed=\"true\"]");
        Assert.Single(active);
        Assert.Contains("dracula", active[0].TextContent);
        Assert.Contains("dracula", html);
    }

    [Fact]
    public void A_junk_stored_value_leaves_the_default_marked()
    {
        // localStorage is the reader's, and this value decides which control draws itself active. The
        // boot script validates before it reaches setAttribute; this is the same guard on the C# side,
        // so a hand-edited value cannot make the picker claim a theme the kit does not ship.
        var js = new FakeJsRuntime();
        js.SetResponse("raskTheme", "not-a-theme");
        var page = RaskTest.Render(() => ThemeMenu, TestServices.Default(js: js));

        var active = page.FindAll("button[aria-pressed=\"true\"]");
        Assert.Single(active);
        Assert.Contains("light", active[0].TextContent);
    }

    [Fact]
    public void The_disclosure_renders_closed_so_a_pick_does_not_leave_it_hanging()
    {
        // Uncontrolled on purpose: a native <details> sets its own `open` when the reader opens it, and
        // because this renders none, the re-render a pick causes morphs the attribute away — which is
        // how a 35-item list closes itself after a choice without any state of its own.
        var page = RaskTest.Render(() => ThemeMenu, TestServices.Default());

        Assert.False(page.Exists("details[open]"));
        Assert.True(page.Exists("details.dropdown"));
    }
}
