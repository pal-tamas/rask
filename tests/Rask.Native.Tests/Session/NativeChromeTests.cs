using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Native.Components;
using Rask.Native.Tests.Infrastructure;
using static Rask.Core.Components.Generated;
using static Rask.Native.Components.Generated;

#pragma warning disable RASK019 // test apps predate framework-managed <head>

namespace Rask.Native.Tests.Session;

// Full-HTML wire shape so tests can assert against the `html` payload field (body effects of a tap/navigate).
[Collection("NativeSession")]
public class NativeChromeTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task InitialRender_PushesHeaderDescriptor()
    {
        var chrome = new FakeNativeChrome();
        _ = await NewSessionAsync<HeaderApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        var d = Assert.Single(chrome.Pushed);
        using var doc = JsonDocument.Parse(d);
        var header = doc.RootElement.GetProperty("header");
        Assert.Equal("Home", header.GetProperty("title").GetString());
        var trailing = header.GetProperty("trailing")[0];
        Assert.Equal("button", trailing.GetProperty("kind").GetString());
        Assert.Equal("h.trailing.0", trailing.GetProperty("id").GetString());
        Assert.Equal("plus", trailing.GetProperty("iosIcon").GetString());
        Assert.Equal("ic_add", trailing.GetProperty("androidIcon").GetString());
    }

    [Fact]
    public async Task NativeWebView_HostsTheHtml_BarsSerializeToNothing()
    {
        // The composed tree serializes to exactly the NativeWebView's HTML shell — the surrounding bars
        // contribute no HTML (they become native chrome, not markup).
        var (_, _, initial) = await NewSessionAsync<HeaderApp>(
            configure: s => s.AddSingleton<INativeChrome>(new FakeNativeChrome()), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(initial.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("added=0", html);            // the shell rendered …
        Assert.DoesNotContain("NativeHeaderBar", html); // … and no native chrome leaked into the HTML.
    }

    [Fact]
    public async Task BarButtonTap_InvokesOnClick_AndDoesNotRepushUnchangedChrome()
    {
        var chrome = new FakeNativeChrome();
        var (_, webView, _) = await NewSessionAsync<HeaderApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        await chrome.TapAsync("h.trailing.0");

        // OnClick ran (body reflects it) …
        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        Assert.Contains("added=1", doc.RootElement.GetProperty("html").GetString()!);
        // … and the (unchanged) header was not re-pushed — no flicker.
        Assert.Single(chrome.Pushed);
    }

    [Fact]
    public async Task ChangedChrome_IsRepushed()
    {
        var chrome = new FakeNativeChrome();
        _ = await NewSessionAsync<DynamicHeaderApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        await chrome.TapAsync("h.trailing.0");

        Assert.Equal(2, chrome.Pushed.Count);
        Assert.Contains("Count 1", chrome.LastJson);
    }

    [Fact]
    public async Task TabTap_NavigatesRoute()
    {
        var chrome = new FakeNativeChrome();
        var (_, webView, _) = await NewSessionAsync<TabApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        // Footer descriptor carries the tab route paths.
        using (var d = JsonDocument.Parse(chrome.Pushed[0]))
        {
            var tabs = d.RootElement.GetProperty("footer").GetProperty("tabs");
            Assert.Equal("/me", tabs[1].GetProperty("path").GetString());
        }

        await chrome.NavigateAsync("/me");

        using var frame = JsonDocument.Parse(webView.LastFrame.AsMemory());
        Assert.Contains("path=/me", frame.RootElement.GetProperty("html").GetString()!);
    }

    [Fact]
    public async Task TabBar_SelectedTracksRoute()
    {
        // TabApp pins no Selected, so the framework derives the active tab from the current route — it must
        // start on Home (index 0) and move to Me (index 1) after navigating, without the page recomputing it.
        var chrome = new FakeNativeChrome();
        _ = await NewSessionAsync<TabApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        using (var d0 = JsonDocument.Parse(chrome.Pushed[0]))
        {
            Assert.Equal(0, d0.RootElement.GetProperty("footer").GetProperty("selected").GetInt32());
        }

        await chrome.NavigateAsync("/me");

        using var d1 = JsonDocument.Parse(chrome.LastJson);
        Assert.Equal(1, d1.RootElement.GetProperty("footer").GetProperty("selected").GetInt32());
    }

    [Fact]
    public async Task BarButtonTap_CanNavigate()
    {
        // A native bar button whose OnClick calls Navigator.NavigateTo must actually navigate — the tap runs
        // inside a Navigator handler scope (like a WebView handler event), so the route change reaches the DOM.
        var chrome = new FakeNativeChrome();
        var (_, webView, _) = await NewSessionAsync<NavButtonApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        await chrome.TapAsync("h.trailing.0");

        using var frame = JsonDocument.Parse(webView.LastFrame.AsMemory());
        Assert.Contains("at=/me", frame.RootElement.GetProperty("html").GetString()!);
    }

    [Fact]
    public async Task LastComposedBarOfAKind_Wins()
    {
        // The bars are composed in the render tree; the last one of a kind in the pre-order walk wins (in
        // practice a single native layout composes the chrome — this just pins the collection rule).
        var chrome = new FakeNativeChrome();
        _ = await NewSessionAsync<TwoHeaderApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(chrome.Pushed[0]);
        Assert.Equal("Inner", doc.RootElement.GetProperty("header").GetProperty("title").GetString());
    }

    [Fact]
    public async Task NoChromeRegistered_AppStillRenders()
    {
        // Backward compatibility: an app can compose native bars with no INativeChrome registered — they are
        // inert (never collected, render no HTML) and the NativeWebView's shell renders normally.
        var (_, webView, initial) = await NewSessionAsync<HeaderApp>(diffMode: DiffMode);

        Assert.NotEmpty(webView.Frames);
        using var doc = JsonDocument.Parse(initial.AsMemory());
        Assert.Contains("added=0", doc.RootElement.GetProperty("html").GetString()!);
    }
}

internal sealed class HeaderApp : Component
{
    private int _added;

    protected override Component? Render() =>
    [
        NativeHeaderBar(Title: "Home", Trailing: [NativeBarButton(Icon: NativeIcon.Add, OnClick: () => _added++)]),
        NativeWebView()[
            Doctype(),
            Html()[Head()[Title()["t"]], Body()[P()[$"added={_added}"]]]
        ]
    ];
}

internal sealed class DynamicHeaderApp : Component
{
    private int _n;

    protected override Component? Render() =>
    [
        NativeHeaderBar(Title: $"Count {_n}", Trailing: [NativeBarButton(Icon: NativeIcon.Add, OnClick: () => _n++)]),
        NativeWebView()[
            Doctype(),
            Html()[Head()[Title()["t"]], Body()[P()[$"n={_n}"]]]
        ]
    ];
}

internal sealed class TabApp : Component
{
    private readonly RouteState _route;

    public TabApp(RouteState route) => _route = route;

    protected override Component? Render() =>
    [
        NativeWebView()[
            Doctype(),
            Html()[Head()[Title()["t"]], Body()[H1()[$"path={_route.Path}"]]]
        ],
        NativeTabBar(
            Tabs:
            [
                NativeTab(Title: "Home", Icon: NativeIcon.Home, To: "/"),
                NativeTab(Title: "Me", Icon: NativeIcon.Person, To: "/me"),
            ])
    ];
}

internal sealed class NavButtonApp : Component
{
    private readonly Navigator _nav;
    private readonly RouteState _route;

    public NavButtonApp(Navigator nav, RouteState route)
    {
        _nav = nav;
        _route = route;
    }

    protected override Component? Render() =>
    [
        NativeHeaderBar(Title: "Nav",
            Trailing: [NativeBarButton(Icon: NativeIcon.Add, OnClick: () => _nav.NavigateTo("/me"))]),
        NativeWebView()[
            Doctype(),
            Html()[Head()[Title()["t"]], Body()[H1()[$"at={_route.Path}"]]]
        ]
    ];
}

internal sealed class TwoHeaderApp : Component
{
    protected override Component? Render() =>
    [
        NativeHeaderBar(Title: "Outer"),
        NativeHeaderBar(Title: "Inner"),
        NativeWebView()[
            Doctype(),
            Html()[Head()[Title()["t"]], Body()[P()["x"]]]
        ]
    ];
}
