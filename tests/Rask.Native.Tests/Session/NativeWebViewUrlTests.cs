using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Native.Components;
using Rask.Native.Tests.Infrastructure;

#pragma warning disable RASK019 // test apps predate framework-managed <head>

namespace Rask.Native.Tests.Session;

/// <summary>
///     A <see cref="NativeWebView" /> carrying a <c>Url</c> gets its UI from a Rask server or a hosted WASM
///     app instead of from components rendering on the device. The session's job is then narrow and
///     specific: navigate once, keep the native bars, and push no HTML — because the document belongs to
///     whatever is serving that address, and diffing against it would be diffing against someone else's page.
/// </summary>
[Collection("NativeSession")]
public class NativeWebViewUrlTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    private static Uri Remote => RemoteUrls.App;

    [Fact]
    public async Task A_url_navigates_the_webview()
    {
        var (_, webView, _) = await NewSessionAsync<UrlApp>(diffMode: DiffMode);

        Assert.Equal(Remote, Assert.Single(webView.LoadedUrls));
    }

    /// <summary>
    ///     The whole reason the mode exists: the page is not ours to render. Pushing HTML at it would fight
    ///     the document the remote app is serving.
    /// </summary>
    [Fact]
    public async Task A_url_frame_pushes_no_html()
    {
        var (_, webView, _) = await NewSessionAsync<UrlApp>(diffMode: DiffMode);

        Assert.Empty(webView.Frames);
    }

    /// <summary>
    ///     A re-render that names the same address must not reload — a reload throws away the page's scroll
    ///     position, its form state and any request in flight, which is a data-loss bug wearing a repaint's
    ///     clothes.
    /// </summary>
    [Fact]
    public async Task Re_rendering_the_same_url_does_not_reload_the_page()
    {
        var (app, webView, _) = await NewSessionAsync<UrlApp>(diffMode: DiffMode);

        await app.Session.RequestRenderAsync();
        await app.Session.RequestRenderAsync();

        Assert.Equal(Remote, Assert.Single(webView.LoadedUrls));
    }

    /// <summary>
    ///     Changing the address is the one thing that should navigate again.
    /// </summary>
    [Fact]
    public async Task Changing_the_url_navigates_again()
    {
        var (app, webView, _) = await NewSessionAsync<SwitchingUrlApp>(diffMode: DiffMode);

        SwitchingUrlApp.Target = RemoteUrls.Other;
        await app.Session.RequestRenderAsync();

        Assert.Equal(
            [Remote, RemoteUrls.Other],
            webView.LoadedUrls);
    }

    /// <summary>
    ///     The point of putting this in markup rather than in the head: the bars are still Rask's, rendered
    ///     natively around a page it did not render.
    /// </summary>
    [Fact]
    public async Task Native_chrome_still_renders_around_a_url_page()
    {
        var chrome = new FakeNativeChrome();
        _ = await NewSessionAsync<UrlApp>(
            configure: s => s.AddSingleton<INativeChrome>(chrome), diffMode: DiffMode);

        var pushed = Assert.Single(chrome.Pushed);
        using var doc = JsonDocument.Parse(pushed);
        Assert.Equal("Remote", doc.RootElement.GetProperty("header").GetProperty("title").GetString());
    }

    /// <summary>
    ///     An app with no <c>Url</c> anywhere is untouched — this mode is opt-in per render, and the ordinary
    ///     markup-hosting path must not have acquired a navigation.
    /// </summary>
    [Fact]
    public async Task A_markup_app_never_navigates()
    {
        var (_, webView, _) = await NewSessionAsync(diffMode: DiffMode);

        Assert.Empty(webView.LoadedUrls);
        Assert.NotEmpty(webView.Frames);
    }

    /// <summary>
    ///     The <c>string</c> overload parses at the call site rather than carrying text to a device and
    ///     failing there as a blank WebView.
    /// </summary>
    [Theory]
    [InlineData("/relative")]              // on Unix this parses ABSOLUTELY, as file:///relative
    [InlineData("app.example.com")]
    [InlineData("not a url at all")]
    [InlineData("javascript:alert(1)")]    // parses absolutely, and would run in a bridged WebView
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("file:///etc/passwd")]
    public void A_url_that_is_not_an_http_address_is_rejected_where_it_is_written(string bad)
    {
        var ex = Assert.Throws<ArgumentException>(() => NativeWebViewUrlProbe.Build(bad));

        Assert.Contains("http:// or https://", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The same guard, reached through the <see cref="Uri" /> step rather than the string one — the
    ///     property is where it lives precisely so neither way in can skip it.
    /// </summary>
    [Fact]
    public void A_non_http_uri_is_rejected_through_the_typed_step()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new NativeWebView { Url = new Uri("javascript:alert(1)") });

        Assert.Contains("http:// or https://", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_url_string_reaches_the_component() =>
        Assert.Equal(Remote, NativeWebViewUrlProbe.Build(Remote.ToString()).Url);

}

// Declared at namespace level, not nested: chain entries are injected into a markup host's own partial, and
// a type nested inside a non-host test class does not get them.

/// <summary>A probe that writes the chain the way an app does, so the string overload is tested as used.</summary>
internal sealed partial class NativeWebViewUrlProbe : Component
{
    public static NativeWebView Build(string url) => NativeWebView.Url(url);
}

/// <summary>
/// A plain holder, not a component: inside a markup host a component's name is the chain's entry, so
/// `UrlApp.Remote` would resolve against `Build&lt;UrlApp&gt;` rather than the type.
/// </summary>
internal static class RemoteUrls
{
    public static readonly Uri App = new("https://app.example.com/");
    public static readonly Uri Other = new("https://other.example.com/");
}

internal sealed partial class UrlApp : Component
{
    protected override Component? Render() =>
    [
        NativeHeaderBar.Title("Remote"),
        NativeWebView.Url(RemoteUrls.App),
    ];
}

internal sealed partial class SwitchingUrlApp : Component
{
    public static Uri Target { get; set; } = RemoteUrls.App;

    protected override Component? Render() =>
    [
        NativeHeaderBar.Title("Remote"),
        NativeWebView.Url(Target),
    ];
}
