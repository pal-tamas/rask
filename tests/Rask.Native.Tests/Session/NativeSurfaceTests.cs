using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Native.Components;
using Rask.Native.Surface;
using Rask.Native.Tests.Infrastructure;
using static Rask.Core.Components.Generated;
using static Rask.Native.Components.Generated;

#pragma warning disable RASK019 // test apps predate framework-managed <head>

namespace Rask.Native.Tests.Session;

/// <summary>
///     The pure-native surface: a page built from <c>NativeScreen</c> and friends renders to real platform
///     views with no WebView involved, and an app may mix the two across routes.
/// </summary>
[Collection("NativeSession")]
public class NativeSurfaceTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task NativeScreen_MountsTheViewTree()
    {
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<ScreenApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        Assert.Equal(["mount"], surface.Calls);
        Assert.False(surface.ShowingWebView);

        var root = surface.Tree!;
        Assert.Equal(NativeNodeKind.Screen, root.Kind);
        var stack = Assert.Single(root.Children);
        Assert.Equal(NativeNodeKind.Stack, stack.Kind);
        Assert.Equal(["count=0", "bump"], stack.Children.ConvertAll(c => c.Text));
        Assert.Equal(
            [NativeNodeKind.Label, NativeNodeKind.Button], stack.Children.ConvertAll(c => c.Kind));
    }

    [Fact]
    public async Task NativeScreen_PushesNoHtmlFrame()
    {
        // The whole point: a native frame must not paint through the WebView. If it did, the WebView's DOM
        // would stop matching the HTML diff baseline and coming back to a web route would repaint it.
        var surface = new FakeNativeSurface();
        var (_, webView, _) = await NewSessionAsync<ScreenApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        Assert.Empty(webView.Frames);
    }

    [Fact]
    public async Task Tap_InvokesOnClick_AndPatchesOnlyWhatChanged()
    {
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<ScreenApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        await surface.TapAsync(TapId(surface));

        Assert.Equal(["mount", "patch:1"], surface.Calls);
        Assert.Equal("count=1", surface.Find(NativeNodeKind.Label)!.Text);
    }

    [Fact]
    public async Task Tap_InvokesOnClickAsync_AndAwaitsItBeforeBuildingTheFrame()
    {
        // The handler yields and only then sets the state the label reads. If the frame were built without
        // awaiting, the patch would carry the pre-await value.
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<AsyncScreenApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        await surface.TapAsync(TapId(surface));

        Assert.Equal("loaded", surface.Find(NativeNodeKind.Label)!.Text);
    }

    [Fact]
    public async Task TextField_Change_DeliversTheNewText()
    {
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<InputApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        var field = surface.Find(NativeNodeKind.TextField)!;
        await surface.ChangeAsync((int)field.Props[NativePropId.ChangeId].Number, "hello");

        Assert.Equal("echo:hello", surface.Find(NativeNodeKind.Label)!.Text);
    }

    [Fact]
    public async Task Switch_Change_DeliversTheNewState()
    {
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<InputApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        var toggle = surface.Find(NativeNodeKind.Switch)!;
        await surface.ChangeAsync((int)toggle.Props[NativePropId.ChangeId].Number, "true");

        Assert.True(surface.Find(NativeNodeKind.Switch)!.Props[NativePropId.On].Flag);
    }

    [Fact]
    public async Task NonInteractiveNode_CarriesNoHandlerId()
    {
        // The prop's ABSENCE is what stops a backend attaching a gesture recognizer at all.
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<ScreenApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        Assert.False(surface.Find(NativeNodeKind.Label)!.Props.ContainsKey(NativePropId.TapId));
        Assert.True(surface.Find(NativeNodeKind.Button)!.Props.ContainsKey(NativePropId.TapId));
    }

    [Fact]
    public async Task UserComponentsAreTransparent_SoAScreenCanBeFactored()
    {
        // MyRow is a plain Component, not a native one. It must not appear in the view tree — its native
        // children must land as if they had been written inline.
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<FactoredApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        var screen = surface.Tree!;
        Assert.Equal(["one", "two"], screen.Children.ConvertAll(c => c.Text));
    }

    [Fact]
    public async Task KeyedRows_ReorderByMoving_NotByRewriting()
    {
        var surface = new FakeNativeSurface();
        _ = await NewSessionAsync<ListApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        Assert.Equal(["a", "b", "c"], surface.Find(NativeNodeKind.List)!.Children.ConvertAll(c => c.Text));

        await surface.TapAsync(TapId(surface));

        Assert.Equal(["c", "a", "b"], surface.Find(NativeNodeKind.List)!.Children.ConvertAll(c => c.Text));
        Assert.Equal(1, surface.MountCount); // reordering never re-mounts the list
    }

    [Fact]
    public async Task WithNoSurfaceRegistered_TheNativeFamilyIsInert()
    {
        // Backward compatibility: an app that never registers INativeSurface behaves exactly as before, and
        // a NativeScreen simply renders nothing rather than throwing.
        var (_, webView, initial) = await NewSessionAsync<ScreenApp>(diffMode: DiffMode);

        Assert.NotEmpty(webView.Frames);
        using var doc = JsonDocument.Parse(initial.AsMemory());
        Assert.DoesNotContain("NativeScreen", doc.RootElement.GetProperty("html").GetString()!);
    }

    // ---- the mixed-surface switch --------------------------------------------------------------------------

    [Fact]
    public async Task WebRouteAndNativeRoute_SwapSurfaces_WithoutEitherBeingRebuilt()
    {
        // The headline scenario: one tab is an HTML page, the next is a pure-native screen. Switching between
        // them must never re-mount the native tree nor reload the WebView — both content views stay alive and
        // are merely hidden, which is exactly what keeps the two diff baselines truthful.
        var surface = new FakeNativeSurface();
        var chrome = new FakeNativeChrome();
        var (_, webView, _) = await NewSessionAsync<MixedApp>(
            configure: s =>
            {
                s.AddSingleton<INativeSurface>(surface);
                s.AddSingleton<INativeChrome>(chrome);
            },
            diffMode: DiffMode);

        // Starts on the web route: the WebView paints, the surface shows it, nothing is mounted natively.
        Assert.True(surface.ShowingWebView);
        Assert.Equal(0, surface.MountCount);
        var webFrames = webView.Frames.Count;
        Assert.True(webFrames > 0);

        // → native route: the tree mounts, and NOT ONE further HTML frame reaches the WebView.
        await chrome.NavigateAsync("/native");
        Assert.False(surface.ShowingWebView);
        Assert.Equal(1, surface.MountCount);
        Assert.Equal("native", surface.Find(NativeNodeKind.Label)!.Text);
        Assert.Equal(webFrames, webView.Frames.Count);

        // → back to the web route: the WebView shows again and the native tree is kept, not torn down.
        await chrome.NavigateAsync("/");
        Assert.True(surface.ShowingWebView);
        Assert.Equal(1, surface.MountCount);

        // → native again: it PATCHES the retained tree instead of mounting a second time.
        await chrome.NavigateAsync("/native");
        Assert.Equal(1, surface.MountCount);
        Assert.False(surface.ShowingWebView);
        Assert.Equal("native", surface.Find(NativeNodeKind.Label)!.Text);
        Assert.Contains(surface.Calls, c => c.StartsWith("patch:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebRoute_StillPaintsHtml_WhenASurfaceIsRegistered()
    {
        // Registering a surface must not disturb the HTML path for the routes that still use it.
        var surface = new FakeNativeSurface();
        var (_, _, initial) = await NewSessionAsync<MixedApp>(
            configure: s => s.AddSingleton<INativeSurface>(surface), diffMode: DiffMode);

        using var doc = JsonDocument.Parse(initial.AsMemory());
        Assert.Contains("web:/", doc.RootElement.GetProperty("html").GetString()!);
    }

    [Fact]
    public async Task NativeHandlerCanNavigate_ToAWebRoute()
    {
        // A tap on a native view that calls Navigator.NavigateTo must actually navigate — including across
        // the surface boundary, from a native screen back to an HTML page.
        var surface = new FakeNativeSurface();
        var (_, webView, _) = await NewSessionAsync<MixedApp>(
            initialPath: "/native",
            configure: s => s.AddSingleton<INativeSurface>(surface),
            diffMode: DiffMode);

        Assert.False(surface.ShowingWebView);
        await surface.TapAsync(TapId(surface));

        Assert.True(surface.ShowingWebView);
        using var doc = JsonDocument.Parse(webView.LastFrame.AsMemory());
        Assert.Contains("web:/", doc.RootElement.GetProperty("html").GetString()!);
    }

    private static int TapId(FakeNativeSurface surface) =>
        (int)surface.Find(NativeNodeKind.Button)!.Props[NativePropId.TapId].Number;
}

internal sealed partial class ScreenApp : Component
{
    private int _count;

    protected override Component? HeadAssets => Title["s"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        NativeScreen[
            NativeStack.Spacing(8)[
                NativeLabel.Text($"count={_count}"),
                NativeButton.Text("bump").OnClick(() => _count++)]];
}

internal sealed partial class AsyncScreenApp : Component
{
    private string _state = "idle";

    protected override Component? HeadAssets => Title["a"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        NativeScreen[
            NativeLabel.Text(_state),
            NativeButton.Text("load").OnClickAsync(async () =>
            {
                await Task.Yield();
                _state = "loaded";
            })];
}

internal sealed partial class InputApp : Component
{
    private string _text = "";
    private bool _on;

    protected override Component? HeadAssets => Title["i"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        NativeScreen[
            NativeLabel.Text($"echo:{_text}"),
            NativeTextField.Value(_text).OnInput(v => _text = v),
            NativeSwitch.On(_on).OnChanged(v => _on = v)];
}

internal sealed partial class FactoredApp : Component
{
    protected override Component? HeadAssets => Title["f"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        NativeScreen[MyRow.RowText("one"), MyRow.RowText("two")];
}

// A plain user component that renders native content — transparent to the view tree.
internal sealed partial class MyRow : Component
{
    public string? RowText { get; set; }

    protected override Component? Render() => NativeLabel.Text(RowText ?? "");
}

internal sealed partial class ListApp : Component
{
    private string[] _items = ["a", "b", "c"];

    protected override Component? HeadAssets => Title["l"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
        NativeScreen[
            NativeButton.Text("rotate").OnClick(() => _items = [_items[2], _items[0], _items[1]]),
            NativeList[_items.Select(i => (Component?)NativeLabel.Text(i).Key(i))]];
}

// One app, two surfaces: "/" is an HTML page, "/native" is a pure-native screen.
internal sealed partial class MixedApp : Component
{
    private readonly Navigator _nav;
    private readonly RouteState _route;

    public MixedApp(Navigator nav, RouteState route)
    {
        _nav = nav;
        _route = route;
    }

    protected override Component? HeadAssets => Title["m"];
    protected override string? HtmlLang => null;

    protected override Component? Render()
    {
        if (_route.Path == "/native")
        {
            return
            [
                NativeScreen[
                    NativeLabel.Text("native"),
                    NativeButton.Text("home").OnClick(() => _nav.NavigateTo("/"))],
                Tabs()
            ];
        }

        return [NativeWebView[H1[$"web:{_route.Path}"]], Tabs()];
    }

    private static Component Tabs() =>
        NativeTabBar.Tabs([
            NativeTab.Title("Web").Icon(NativeIcon.Home).To("/"),
            NativeTab.Title("Native").Icon(NativeIcon.Person).To("/native"),
        ]);
}
