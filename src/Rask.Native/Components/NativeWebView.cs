using Rask.Core;

namespace Rask.Native.Components;

/// <summary>
///     The web surface of a native page. It has two modes, and they are mutually exclusive:
///     <list type="number">
///         <item>
///             <b>Markup</b> — its children are the ordinary page content, which the native host serializes
///             into the platform WebView (Rask composes the document around the whole render, exactly as on
///             the web):
///             <code>Render() => [NativeHeaderBar.Title("Home"), NativeWebView[Router], NativeTabBar(…)];</code>
///             It renders its children transparently (like a fragment), so on a web host — where you branch
///             with <c>IsNative</c> and return the content alone — the same markup serializes identically.
///         </item>
///         <item>
///             <b>URL</b> — set <see cref="Url" /> and the WebView loads that address instead, so the UI
///             comes from a Rask server or a hosted WASM app you deploy rather than from components running
///             on the device:
///             <code>Render() => [NativeHeaderBar.Title("Home"), NativeWebView.Url("https://app.example.com")];</code>
///             The native bars still render natively around it, and the page reaches the device backends
///             through the capability bridge, so the app is native everywhere except where the UI comes from.
///         </item>
///     </list>
///     Only native chrome (bars) may sit outside it; putting a bar inside its HTML content is a
///     <c>RASK032</c> error. Setting <see cref="Url" /> <em>and</em> passing children is <c>RASK049</c>, and
///     using both modes anywhere in one app is <c>RASK050</c> — the WebView holds one document, so the two
///     cannot both be true of it.
/// </summary>
/// <remarks>
///     <b>Security.</b> The page loaded from <see cref="Url" /> is given the native capability bridge, so it
///     can reach the device backends this app registered. Point it at an origin you control. The head keeps
///     the WebView on that origin — an off-origin link opens in the system browser instead — but that
///     confines where the bridge travels, not what the page you named can do with it.
/// </remarks>
public sealed partial class NativeWebView : NativeComponent
{
    /// <summary>
    ///     An absolute URL to load into the WebView instead of hosting markup — a Rask server, or a WASM app
    ///     you host. Leave it unset to use the children instead.
    ///     <para>
    ///         Nullable on purpose: a non-nullable property with no initializer would become a required chain
    ///         step (RASK001/RASK038), which every existing <c>NativeWebView[…]</c> would then fail to take.
    ///     </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The value is not an absolute <c>http</c>/<c>https</c> URL. Checked in the setter rather than in the
    ///     chain step so both ways in are covered — the generated <c>.Url(Uri)</c> step and a plain
    ///     assignment — and so a <c>javascript:</c>, <c>data:</c> or <c>file:</c> address cannot reach a
    ///     WebView that carries the capability bridge.
    /// </exception>
    public Uri? Url
    {
        get;
        set
        {
            if (value is not null
                && (!value.IsAbsoluteUri
                    || (!string.Equals(value.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))))
            {
                throw new ArgumentException(
                    $"'{value}' is not an absolute http:// or https:// URL. NativeWebView.Url takes the "
                    + "address of the app the WebView should load, e.g. \"https://app.example.com/\".",
                    nameof(value));
            }

            field = value;
        }
    }

    // Transparent HTML host: emit the children (the page shell) inline. A native component with children can't
    // reuse the render cache (see Component.RenderForLive), so this re-projects on each render. In URL mode
    // there are no children and nothing to render — the session loads the address instead, and a frame that
    // renders no HTML is exactly what tells it to.
    protected override Component? Render() => Children is null ? null : [.. Children];
}
