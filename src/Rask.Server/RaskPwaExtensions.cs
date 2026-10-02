using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Browser;
using Rask.Core.Components;
using Rask.Core.Live;
namespace Rask.Server;

/// <summary>
///     Opt-in Progressive Web App support for the Rask Server host — the server-side counterpart to the
///     WASM host's <c>WasmHostBuilder.UsePwa(...)</c>. Call <see cref="AddRaskPwa" /> alongside
///     <c>AddRask()</c> to make a Server app installable and push-capable.
/// </summary>
/// <remarks>
///     What you get on Server: an installable <see cref="WebAppManifest" /> (served at
///     <c>{PathBase}/rask/manifest.webmanifest</c> and linked from the server-rendered <c>&lt;head&gt;</c>),
///     a service worker at <c>{PathBase}/rask-sw.js</c> — registered by the page's head, so
///     <c>await Navigator.ServiceWorker.Ready</c> reaches it — that shows Web Push and serves a static
///     <c>offline.html</c> for failed navigations, and the transport-agnostic PWA APIs (MDN's
///     <c>PushManager</c>/<c>Notification</c>/<c>Navigator.SetAppBadge</c> from Rask.Web, and
///     <see cref="IWakeLock" />, registered by <c>AddRask()</c>).
///     <para>
///         What you do NOT get (a Server app renders over a live WebSocket): a true offline app — the SW
///         deliberately does not cache the server-rendered shell (it carries a one-shot session id and is
///         served <c>no-store</c>), so offline navigations show <c>offline.html</c> rather than a dead
///         cached page — and no background sync or install-prompt replay (those stay WASM-only).
///     </para>
/// </remarks>
public static class RaskPwaExtensions
{
    /// <summary>
    ///     Enables PWA support for the Server host from a typed <see cref="WebAppManifest" />. Registers the
    ///     manifest and a head contribution that emits <c>&lt;link rel="manifest"&gt;</c> +
    ///     <c>&lt;meta name="theme-color"&gt;</c>; <c>MapRask&lt;TApp&gt;()</c> then serves the manifest and
    ///     service-worker endpoints. PWA stays off unless this is called.
    /// </summary>
    /// <param name="services">The app's service collection (after <c>AddRask()</c>).</param>
    /// <param name="manifest">The installable web app manifest (name, icons, theme color, …).</param>
    public static IServiceCollection AddRaskPwa(this IServiceCollection services, WebAppManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        services.AddSingleton(new RaskPwaState(manifest));
        services.AddSingleton<IRaskHeadContribution, RaskPwaHeadContribution>();
        return services;
    }
}
