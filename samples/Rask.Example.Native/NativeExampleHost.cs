using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Shared;
using Rask.Native;

namespace Rask.Example.Native;

/// <summary>
///     The native composition root for the showcase. Mirrors what <c>Rask.Example.Server</c>'s
///     <c>Program.cs</c> and <c>Rask.Example.Wasm</c>'s <c>Program.cs</c> do — register the shared
///     <see cref="ExampleServiceCollectionExtensions.AddExampleServices" /> demo services and mount the
///     shared <see cref="App" /> — but onto a <see cref="NativeAppHost" /> instead of an ASP.NET / WASM
///     host. On a device the app head calls <see cref="Create" /> then
///     <c>host.RunLocalAsync&lt;App&gt;(webView)</c>; the E2E harness does the same against a
///     Playwright-backed <c>INativeWebView</c>.
/// </summary>
public static class NativeExampleHost
{
    /// <summary>
    ///     The synthetic app origin the native shell + client + scoped/static assets are served from — a
    ///     <c>WKUrlSchemeHandler</c> / <c>WebViewAssetLoader</c> on device, a Playwright route in the E2E
    ///     harness. The demo <c>HttpClient</c>'s base address points here so <c>data/*.json</c> fetches
    ///     resolve against the same secure origin.
    /// </summary>
    public const string AppOrigin = "https://native.local/";

    /// <summary>Builds a <see cref="NativeAppHost" /> with the shared showcase services registered.</summary>
    public static NativeAppHost Create(string appOrigin = AppOrigin)
    {
        var host = NativeAppHost.CreateDefault();
        host.Services.AddExampleServices(_ => new Uri(appOrigin));
        return host;
    }
}
