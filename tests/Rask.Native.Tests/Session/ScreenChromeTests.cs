using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Native.Components;
using Rask.Native.Tests.Infrastructure;
using static Rask.Core.Components.Generated;
using static Rask.Native.Components.Generated;

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
    public async Task SlotsAreNotReadWithoutAChromeBackend()
    {
        // No INativeChrome registered → the host does not collect chrome and the overrides are never
        // evaluated. Same contract the sibling-composed bars have, and what makes one Screen class serve
        // web and native without an IsNative branch.
        var (_, _, initial) = await NewSessionAsync<ThrowingChromeScreenApp>(diffMode: DiffMode);

        using var doc = JsonDocument.Parse(initial.AsMemory());
        Assert.Contains("body-rendered", doc.RootElement.GetProperty("html").GetString()!);
    }
}

internal sealed partial class ChromeScreenApp : Screen
{
    private int _added;

    protected override string Route => "/screen-chrome";
    protected override Component? HeadAssets => Title["t"];
    protected override string? HtmlLang => null;

    protected override Component? HeaderBar =>
        NativeHeaderBar.Title("Home").Trailing([NativeBarButton.Icon(NativeIcon.Add).OnClick(() => _added++)]);

    protected override Component? Render() => NativeWebView[P[$"added={_added}"]];
}

internal sealed partial class LeafScreen : Screen
{
    protected override string Route => "/screen-chrome-leaf";

    protected override Component? HeaderBar => NativeHeaderBar.Title("Leaf");

    protected override Component? Render() => P["leaf"];
}

internal sealed partial class LayoutScreenApp : Screen
{
    protected override string Route => "/screen-chrome-layout";
    protected override Component? HeadAssets => Title["t"];
    protected override string? HtmlLang => null;

    protected override Component? HeaderBar => NativeHeaderBar.Title("Layout");

    protected override Component? Render() => NativeWebView[LeafScreen];
}

internal sealed partial class ThrowingChromeScreenApp : Screen
{
    protected override string Route => "/screen-chrome-unread";
    protected override Component? HeadAssets => Title["t"];
    protected override string? HtmlLang => null;

    protected override Component? HeaderBar =>
        throw new InvalidOperationException("chrome slot must not be read without an INativeChrome backend");

    protected override Component? Render() => P["body-rendered"];
}
