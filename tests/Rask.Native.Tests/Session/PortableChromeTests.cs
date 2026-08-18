using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Chrome;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Native.Tests.Infrastructure;
using static Rask.Chrome.Components.Generated;
using static Rask.Core.Components.Generated;
// An alias rather than a namespace import, but not because the import breaks: entries are injected into
// this partial class, and a member of the enclosing type wins simple-name lookup over a namespace-imported
// type, so `using Rask.Chrome.Components;` compiles here too (the sibling ScreenChromeTests imports
// Rask.Native.Components exactly that way). The alias only avoids leaning on that ordering for the one type
// actually needed — BarIcon is a struct, so no entry exists for it.
using BarIcon = Rask.Chrome.Components.BarIcon;

#pragma warning disable RASK019 // test apps predate framework-managed <head>

namespace Rask.Native.Tests.Session;

/// <summary>
///     The portable chrome vocabulary — <c>AppBar</c> / <c>TabStrip</c> / <c>BarButton</c> / <c>TabItem</c>,
///     all in <c>Rask.Chrome</c> — projected to real platform bars by this host. The point of the exercise is
///     that the screen declaring them contains no <c>Rask.Native</c> type at all, so the same class compiles
///     and renders on the Server and WASM heads (where it emits HTML instead; see
///     <c>Rask.Chrome.Tests.ChromeBarTests</c>).
/// </summary>
[Collection("NativeSession")]
public class PortableChromeTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task AppBarSlot_ReachesTheChromeDescriptor()
    {
        var chrome = new FakeNativeChrome();
        _ = await NativeSessionHarness.NewSessionAsync<PortableChromeApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(Assert.Single(chrome.Pushed));
        var header = doc.RootElement.GetProperty("header");
        Assert.Equal("Todos", header.GetProperty("title").GetString());
        // The icon arrives already resolved to both platform tokens — the head does no lookup.
        var trailing = header.GetProperty("trailing")[0];
        Assert.Equal("plus", trailing.GetProperty("iosIcon").GetString());
        Assert.Equal("ic_add", trailing.GetProperty("androidIcon").GetString());
        Assert.Equal("New", trailing.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AppBarSlot_ContributesNoHtml()
    {
        // The bar is a real platform widget here; markup would leave a duplicate header inside the WebView.
        var (_, _, initial) = await NativeSessionHarness.NewSessionAsync<PortableChromeApp>(
            configure: s => s.AddSingleton<INativeChrome>(new FakeNativeChrome()), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(initial.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("added=0", html);
        Assert.DoesNotContain("rask-header-bar", html);
        Assert.DoesNotContain("rask-tab-bar", html);
    }

    [Fact]
    public async Task PortableBarButton_RunsItsOnClick_AndRerendersTheScreen()
    {
        // The slot is walked inside the screen's own scope, so the callback attributes back to the screen —
        // identical to the Rask.Native bar buttons, and to a button in the body.
        var chrome = new FakeNativeChrome();
        var (_, webView, _) = await NativeSessionHarness.NewSessionAsync<PortableChromeApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        await chrome.TapAsync("h.trailing.0");

        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        Assert.Contains("added=1", doc.RootElement.GetProperty("html").GetString()!);
    }

    [Fact]
    public async Task TabStripSlot_ProjectsTabsWithTheirRoutes()
    {
        var chrome = new FakeNativeChrome();
        _ = await NativeSessionHarness.NewSessionAsync<PortableChromeApp>(
            "/todos", configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(Assert.Single(chrome.Pushed));
        var footer = doc.RootElement.GetProperty("footer");
        Assert.Equal("tabbar", footer.GetProperty("kind").GetString());
        var tabs = footer.GetProperty("tabs");
        Assert.Equal(2, tabs.GetArrayLength());
        Assert.Equal("/todos", tabs[1].GetProperty("path").GetString());
        Assert.Equal("3", tabs[1].GetProperty("badge").GetString());
    }

    // The selected tab comes from Rask.Core's TabStrip.DeriveSelected — the same method the web hosts call —
    // so one declaration cannot light a different tab depending on which head is running it.
    [Theory]
    [InlineData("/", 0)]
    [InlineData("/todos", 1)]
    [InlineData("/todos/42", 1)]
    public async Task TabStripSelection_TracksTheRoute(string path, int expected)
    {
        var chrome = new FakeNativeChrome();
        _ = await NativeSessionHarness.NewSessionAsync<PortableChromeApp>(
            path, configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(Assert.Single(chrome.Pushed));
        Assert.Equal(expected, doc.RootElement.GetProperty("footer").GetProperty("selected").GetInt32());
    }
}

// Deliberately free of any Rask.Native type: this is the class that is supposed to compile for the web heads
// too. It names only Rask.Chrome / Rask.Core components.
internal sealed partial class PortableChromeApp : Screen
{
    private int _added;

    protected override string Route => "/portable-chrome";
    protected override Component? HeadAssets => Title["t"];
    protected override string? HtmlLang => null;

    protected override Component? HeaderBar =>
        AppBar.Title("Todos").Trailing([BarButton.Icon(BarIcon.Add).Title("New").OnClick(() => _added++)]);

    protected override Component? TabBar =>
        TabStrip.Tabs([
            TabItem.Title("Home").Icon(BarIcon.Home).To(new RouteUrl("/")),
            TabItem.Title("Todos").Icon(BarIcon.List).To(new RouteUrl("/todos")).Badge("3"),
        ]);

    protected override Component? Render() => P[$"added={_added}"];
}
