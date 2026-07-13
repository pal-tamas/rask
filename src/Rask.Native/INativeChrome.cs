namespace Rask.Native;

/// <summary>
///     Optional backend that projects the <c>NativeHeaderBar</c> / <c>NativeTabBar</c> a page composes to real platform bars
///     (iOS <c>UINavigationBar</c>/<c>UITabBar</c>/<c>UIToolbar</c>, Android <c>MaterialToolbar</c>/
///     <c>BottomNavigationView</c>). Register an implementation on <c>host.Services</c> before
///     <c>RunLocalAsync</c> — exactly like <c>IShare</c> — to opt a native app into header/footer chrome. With
///     no registration the feature is inert (no bars; the WebView fills the screen), so it is fully backward
///     compatible. The platform WebView heads (<c>RaskWkWebView</c>/<c>RaskAndroidWebView</c>) implement it.
/// </summary>
public interface INativeChrome
{
    /// <summary>
    ///     Apply the latest chrome to the native bars. <paramref name="chromeDescriptorUtf8" /> is a UTF-8 JSON
    ///     <c>NativeChromeDescriptor</c> (header/footer with titles, icons, and tap ids). Called on the render
    ///     thread after each frame whose chrome changed; the implementation marshals to the UI thread.
    /// </summary>
    ValueTask ApplyChromeAsync(ReadOnlyMemory<byte> chromeDescriptorUtf8);

    /// <summary>
    ///     Set by the host to receive bar interactions. The head raises a UTF-8 JSON message in the same wire
    ///     shape as WebView events — <c>{"type":"nativeTap","id":"…"}</c> for a bar button,
    ///     <c>{"type":"navigate","path":"…"}</c> for a tab — so it re-enters the existing router/dispatch.
    /// </summary>
    Func<byte[], Task>? OnChromeEvent { get; set; }
}
