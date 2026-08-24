using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Native.Components;
using Rask.Native.Surface;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

/// <summary>
///     Booting with <b>no WebView at all</b> — the pure-native model (#777). The session used to require an
///     <c>INativeWebView</c> in its constructor, push every frame through it, and implement back navigation
///     as <c>window.history.back()</c>, so a WebView-less app was unrepresentable.
/// </summary>
[Collection("NativeSession")]
public sealed class PureNativeBootTests : ResettingTestBase
{
    [Fact]
    public async Task An_app_boots_and_paints_with_no_WebView_instantiated()
    {
        var (app, surface) = await NewPureNativeAsync();

        // The first render happens without a `ready` handshake — there is no client to send one.
        Assert.Equal("mount", Assert.Single(surface.Calls));
        Assert.NotNull(surface.Tree);
        Assert.False(surface.ShowingWebView);

        await app.DisposeAsync();
    }

    [Fact]
    public async Task A_tap_re_renders_through_the_surface_alone()
    {
        var (app, surface) = await NewPureNativeAsync();
        var tapId = FindTapId(surface.Tree!);
        Assert.True(tapId >= 0, "the button should carry a tap handler id");

        await surface.OnSurfaceEvent!(
            new NativeSurfaceEvent(tapId, NativeSurfaceEventKind.Tap, null));

        // A patch, not a re-mount: the tree is retained across the interaction exactly as on a device.
        Assert.Equal(1, surface.MountCount);
        Assert.Contains(surface.Calls, c => c.StartsWith("patch:", StringComparison.Ordinal));

        await app.DisposeAsync();
    }

    /// <summary>
    ///     Back with no page history to pop. The session keeps its own, so Android's hardware Back button
    ///     has something to act on.
    /// </summary>
    [Fact]
    public async Task Back_navigation_walks_the_sessions_own_history()
    {
        var (app, _) = await NewPureNativeAsync();
        var routeState = app.Services.GetRequiredService<RouteState>();

        Assert.True(app.Session.OwnsBackHistory);
        Assert.Equal(["/"], app.Session.BackHistory);

        await app.Session.DispatchAsync(Message("""{"type":"navigate","path":"/second"}"""));
        Assert.Equal(["/", "/second"], app.Session.BackHistory);
        Assert.Equal("/second", routeState.Path);

        await app.Session.GoBackAsync();

        Assert.Equal(["/"], app.Session.BackHistory);
        Assert.Equal("/", routeState.Path);

        await app.DisposeAsync();
    }

    /// <summary>
    ///     At the first entry back does nothing, so on Android the hardware button falls through to the
    ///     activity and closes the app — the platform behaviour at the root of a task. Swallowing it would
    ///     trap the user.
    /// </summary>
    [Fact]
    public async Task Back_at_the_first_entry_does_nothing()
    {
        var (app, _) = await NewPureNativeAsync();

        await app.Session.GoBackAsync();

        Assert.Equal(["/"], app.Session.BackHistory);
        Assert.Equal("/", app.Services.GetRequiredService<RouteState>().Path);

        await app.DisposeAsync();
    }

    [Fact]
    public async Task Navigating_to_where_you_already_are_is_not_a_history_entry()
    {
        var (app, _) = await NewPureNativeAsync();

        await app.Session.DispatchAsync(Message("""{"type":"navigate","path":"/second"}"""));
        await app.Session.DispatchAsync(Message("""{"type":"navigate","path":"/second"}"""));

        // Otherwise back would have to be pressed twice to move once.
        Assert.Equal(["/", "/second"], app.Session.BackHistory);

        await app.DisposeAsync();
    }

    [Fact]
    public async Task A_replace_navigation_overwrites_the_current_entry()
    {
        var (app, _) = await NewPureNativeAsync();

        await app.Session.DispatchAsync(Message("""{"type":"navigate","path":"/second"}"""));
        await app.Session.DispatchAsync(
            Message("""{"type":"navigate","path":"/third","replace":true}"""));

        Assert.Equal(["/", "/third"], app.Session.BackHistory);

        await app.DisposeAsync();
    }

    /// <summary>
    ///     The WebView-hybrid model keeps exactly one history — the page's — so the session must not start
    ///     a second one that could disagree with it.
    /// </summary>
    [Fact]
    public async Task A_session_with_a_WebView_keeps_no_history_of_its_own()
    {
        var (app, _, _) = await NativeSessionHarness.NewSessionAsync();

        Assert.False(app.Session.OwnsBackHistory);
        await app.Session.DispatchAsync(Message("""{"type":"navigate","path":"/second"}"""));
        Assert.Empty(app.Session.BackHistory);

        await app.DisposeAsync();
    }

    /// <summary>
    ///     Rendering HTML with no WebView is a real mistake with a silent failure mode, so it names itself.
    /// </summary>
    [Fact]
    public async Task Rendering_HTML_with_no_WebView_says_so()
    {
        var host = NativeAppHost.CreateDefault();
        var surface = new FakeNativeSurface();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.RunNativeAsync<NativeStubApp>(surface));

        Assert.Contains("pure-native", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NativeScreen", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     And so does calling <c>IJSRuntime</c>, rather than reporting a session-scope problem the app
    ///     does not have.
    /// </summary>
    [Fact]
    public async Task Calling_IJSRuntime_with_no_WebView_says_so()
    {
        var (app, _) = await NewPureNativeAsync();
        var js = app.Services.GetRequiredService<IJSRuntime>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await js.InvokeVoidAsync("alert", "hi"));

        Assert.Contains("pure-native", ex.Message, StringComparison.Ordinal);
        Assert.Contains("no JavaScript engine", ex.Message, StringComparison.Ordinal);

        await app.DisposeAsync();
    }

    private static byte[] Message(string json) => System.Text.Encoding.UTF8.GetBytes(json);

    private static async Task<(NativeApp App, FakeNativeSurface Surface)> NewPureNativeAsync()
    {
        var host = NativeAppHost.CreateDefault();
        var surface = new FakeNativeSurface();
        var app = await host.RunNativeAsync<PureNativeStubApp>(surface);
        return (app, surface);
    }

    private static int FindTapId(FakeNativeSurface.MutableNode node)
    {
        if (node.Props.TryGetValue(NativePropId.TapId, out var tap))
        {
            return (int)tap.Number;
        }

        foreach (var child in node.Children)
        {
            var found = FindTapId(child);
            if (found >= 0)
            {
                return found;
            }
        }

        return -1;
    }
}

// A tree that is pure-native all the way down: no HTML anywhere, so nothing ever asks for a WebView.
internal sealed partial class PureNativeStubApp : Component
{
    private readonly RouteState _routeState;

    private int _taps;

    public PureNativeStubApp(RouteState routeState) => _routeState = routeState;

    protected override Component? Render() =>
        NativeScreen[
            NativeStack[
                NativeLabel[$"path={_routeState.Path}"],
                NativeLabel[$"taps={_taps}"],
                NativeButton.OnClick(() => _taps++)["bump"]
            ]
        ];
}
