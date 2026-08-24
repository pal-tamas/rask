using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Chrome;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Native.Components;
using Rask.Native.Tests.Infrastructure;

#pragma warning disable RASK019 // test apps predate framework-managed <head>

namespace Rask.Native.Tests.Session;

/// <summary>
///     A <see cref="Screen" /> declares its own native chrome through hoisted slots, instead of the app root
///     inspecting the current path to decide what the header should show.
/// </summary>
[Collection("NativeSession")]
public class ScreenChromeTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task HeaderBarSlot_ReachesTheChromeDescriptor()
    {
        var chrome = new FakeNativeChrome();
        _ = await NewSessionAsync<ChromeScreenApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        var d = Assert.Single(chrome.Pushed);
        using var doc = JsonDocument.Parse(d);
        Assert.Equal("Home", doc.RootElement.GetProperty("header").GetProperty("title").GetString());
    }

    [Fact]
    public async Task HeaderBarSlot_ContributesNoHtml()
    {
        var (_, _, initial) = await NewSessionAsync<ChromeScreenApp>(
            configure: s => s.AddSingleton<INativeChrome>(new FakeNativeChrome()), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(initial.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("added=0", html);
        Assert.DoesNotContain("NativeHeaderBar", html);
    }

    [Fact]
    public async Task SlotBarButton_RunsItsOnClick_AndRerendersTheScreen()
    {
        // The slot is walked inside the screen's own scope, so a bar button's callback attributes back to
        // the screen exactly like one composed in Render().
        var chrome = new FakeNativeChrome();
        var (_, webView, _) = await NewSessionAsync<ChromeScreenApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        await chrome.TapAsync("h.trailing.0");

        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        Assert.Contains("added=1", doc.RootElement.GetProperty("html").GetString()!);
    }

    [Fact]
    public async Task NestedScreen_HeaderWins_OverTheOuterScreens()
    {
        // Chrome merges by kind, deepest-wins — which is what lets a layout screen own the tab bar while
        // each leaf screen owns its header.
        var chrome = new FakeNativeChrome();
        _ = await NewSessionAsync<LayoutScreenApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        var d = Assert.Single(chrome.Pushed);
        using var doc = JsonDocument.Parse(d);
        Assert.Equal("Leaf", doc.RootElement.GetProperty("header").GetProperty("title").GetString());
    }

    [Fact]
    public async Task SlotsAreEvaluatedEvenWithoutAChromeBackend_ButContributeNoHtml()
    {
        // The slots are walked on EVERY host now, not only where chrome is collected. That is what lets the
        // portable Rask.Core bars (AppBar / TabStrip) render real markup on the web heads from the same
        // declaration a native head projects to platform bars — they could not, while these overrides were
        // read on the native host alone.
        //
        // The Rask.Native bars keep costing nothing wherever they are read: their Render() returns null.
        var (_, _, initial) = await NewSessionAsync<ChromeScreenApp>(diffMode: DiffMode);

        using var doc = JsonDocument.Parse(initial.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("added=0", html);
        Assert.DoesNotContain("Home", html);
    }
}

[Route("/screen-chrome")]
internal sealed partial class ChromeScreenApp : Screen
{
    private int _added;

    protected override Component? HeadAssets => Title["t"];
    protected override string? HtmlLang => null;

    protected override Component? HeaderBar =>
        NativeHeaderBar.Title("Home").Trailing([NativeBarButton.Icon(NativeIcon.Add).OnClick(() => _added++)]);

    protected override Component? Render() => NativeWebView[P[$"added={_added}"]];
}

[Route("/screen-chrome-leaf")]
internal sealed partial class LeafScreen : Screen
{
    protected override Component? HeaderBar => NativeHeaderBar.Title("Leaf");

    protected override Component? Render() => P["leaf"];
}

[Route("/screen-chrome-layout")]
internal sealed partial class LayoutScreenApp : Screen
{
    protected override Component? HeadAssets => Title["t"];
    protected override string? HtmlLang => null;

    protected override Component? HeaderBar => NativeHeaderBar.Title("Layout");

    protected override Component? Render() => NativeWebView[LeafScreen];
}

