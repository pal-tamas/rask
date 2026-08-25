using Rask.Core;
using Rask.Native.Components;

// Global namespace, exactly like the generated RaskBuilderSetters classes: an extension method is only
// found when its containing namespace is in scope, and the global namespace encloses every namespace — so
// `NativeWebView.Url("https://…")` needs no using directive, the same way `.Class(…)` does not.

/// <summary>
///     The <c>string</c> overload of <see cref="NativeWebView.Url" />. The property is typed
///     <see cref="Uri" /> so the address is parsed once, at the call site, rather than carried as text and
///     discovered to be malformed on a device — but writing a URL as a literal is the common case, so the
///     chain takes both.
/// </summary>
public static class RaskNativeWebViewUrlSetters
{
    /// <summary>
    ///     Point the WebView at an absolute URL — a Rask server, or a WASM app you host.
    /// </summary>
    /// <param name="build">The chain.</param>
    /// <param name="url">
    ///     An absolute <c>http</c> or <c>https</c> URL, e.g. <c>https://app.example.com/</c>. Anything else
    ///     throws here, where the call site is, instead of failing later as a blank WebView.
    /// </param>
    /// <exception cref="ArgumentException">The value is not an absolute http/https URL.</exception>
    public static Build<NativeWebView> Url(this Build<NativeWebView> build, string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        // Absolute, and a web scheme. `IsAbsoluteUri` alone is not enough on either count: on Unix
        // Uri.TryCreate("/relative", Absolute, …) SUCCEEDS as file:///relative, and `javascript:` and `data:`
        // parse absolutely too. All three would be handed to a WebView that carries the capability bridge, so
        // the scheme is checked rather than assumed.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || !IsWebScheme(parsed))
        {
            throw new ArgumentException(
                $"'{url}' is not an absolute URL. NativeWebView.Url takes the full http:// or https:// "
                + "address of the app the WebView should load, e.g. \"https://app.example.com/\".",
                nameof(url));
        }

        return build.Url(parsed);
    }

    private static bool IsWebScheme(Uri url) =>
        string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
