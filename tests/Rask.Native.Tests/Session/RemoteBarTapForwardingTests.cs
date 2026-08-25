using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Html.Components;
using Rask.Native.Components;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

/// <summary>
///     A press on a bar this process did not describe is handed back to the page, which forwards it to the
///     hosted app that owns the callback — and a press on a local bar is not.
/// </summary>
/// <remarks>
///     <para>
///         Found on a device, not in a test. Every unit around this passed — the head applied the remote
///         descriptor, the forwarding script was correct, the server ran the tap it was sent — because the
///         one broken link was the <em>wiring</em>: the host handed the chrome backend a router with no
///         reply channel, so the branch that forwards a remote tap could never be reached. The bar drew
///         perfectly and did nothing when pressed.
///     </para>
///     <para>
///         So these press a bar through <c>OnChromeEvent</c> on a really-wired host — the channel a platform
///         bar actually uses — rather than calling the forwarding helper directly, which is exactly the test
///         that would have passed while the app was broken.
///     </para>
/// </remarks>
[Collection("NativeSession")]
public class RemoteBarTapForwardingTests : ResettingTestBase
{
    private static async Task<(NativeApp App, FakeNativeWebView WebView, FakeNativeChrome Chrome)>
        RunAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>()
        where TApp : Component
    {
        var host = NativeAppHost.CreateDefault();
        var chrome = new FakeNativeChrome();
        host.Services.AddSingleton<INativeChrome>(chrome);

        var webView = new FakeNativeWebView();
        var app = await host.RunLocalAsync<TApp>(webView);
        await webView.PostAsync("""{"type":"ready"}""");

        return (app, webView, chrome);
    }

    private static bool Forwarded(FakeNativeWebView webView, string id) =>
        webView.Evaluated.Exists(script =>
            script.Contains("__raskNative.chromeTap", StringComparison.Ordinal)
            && script.Contains(id, StringComparison.Ordinal));

    [Fact]
    public async Task A_tap_this_session_did_not_describe_is_forwarded_to_the_page()
    {
        var (app, webView, chrome) = await RunAsync<PlainApp>();

        // An id from a bar a hosted app described. This session declared no bars at all, so the only thing
        // it can do with the press is pass it back the way it came.
        await chrome.TapAsync("h.trailing.0");

        Assert.True(Forwarded(webView, "h.trailing.0"),
            "a bar this session never described was swallowed instead of forwarded");

        await app.DisposeAsync();
    }

    /// <summary>
    ///     The other half. A bar this session DID describe must not be forwarded: its callback is right
    ///     here, and sending the press onward as well would run it twice.
    /// </summary>
    [Fact]
    public async Task A_tap_on_a_local_bar_runs_here_and_is_not_forwarded()
    {
        var (app, webView, chrome) = await RunAsync<LocalBarApp>();

        Assert.NotEmpty(chrome.Pushed);
        // The id this session actually minted, read back from the descriptor it pushed — an invented one
        // would take the "not mine" branch and pass the test for the wrong reason.
        using var descriptor = JsonDocument.Parse(chrome.LastJson);
        var id = descriptor.RootElement
            .GetProperty("header").GetProperty("trailing")[0].GetProperty("id").GetString()!;

        await chrome.TapAsync(id);

        Assert.False(Forwarded(webView, id), "a local bar's press was forwarded as if it were remote");
        Assert.Contains(webView.Frames, frame =>
            System.Text.Encoding.UTF8.GetString(frame).Contains("tapped=1", StringComparison.Ordinal));

        await app.DisposeAsync();
    }
}

/// <summary>An app with no bars of its own — whatever chrome appears came from somewhere else.</summary>
internal sealed partial class PlainApp : Component
{
    protected override Component? Render() => NativeWebView[Div["body"]];
}

internal sealed partial class LocalBarApp : Component
{
    private int _taps;

    protected override Component? Render() =>
    [
        NativeHeaderBar.Title("Local").Trailing([NativeBarButton.Icon(NativeIcon.Star).Title("Add").OnClick(() => _taps++)]),
        NativeWebView[Div[$"tapped={_taps}"]],
    ];
}
