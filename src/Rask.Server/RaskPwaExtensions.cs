using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Browser;
using Rask.Core.Components;
using Rask.Core.Live;
using Components = Rask.Core.Components.Generated;

namespace Rask.Server;

/// <summary>
///     Opt-in Progressive Web App support for the Rask Server host — the server-side counterpart to the
///     WASM host's <c>WasmHostBuilder.UsePwa(...)</c>. Call <see cref="AddRaskPwa" /> alongside
///     <c>AddRask()</c> to make a Server app installable and push-capable.
/// </summary>
/// <remarks>
///     What you get on Server: an installable <see cref="WebAppManifest" /> (served at
///     <c>{PathBase}/rask/manifest.webmanifest</c> and linked from the server-rendered <c>&lt;head&gt;</c>),
///     a service worker at <c>{PathBase}/rask-sw.js</c> that handles Web Push and serves a static
///     <c>offline.html</c> for failed navigations, and the transport-agnostic PWA APIs
///     (<see cref="IWebPush" />/<see cref="INotifications" />/<see cref="IBadge" />/<see cref="IWakeLock" />,
///     registered by <c>AddRask()</c>).
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
    ///     <c>&lt;meta name="theme-color"&gt;</c>; <c>UseRask&lt;TApp&gt;()</c> then serves the manifest and
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

/// <summary>
///     Holds the opted-in <see cref="WebAppManifest" />. Its presence in DI is the "PWA enabled" flag the
///     request pipeline checks — the manifest/service-worker endpoints and the head contribution are only
///     wired when this singleton is registered (via <see cref="RaskPwaExtensions.AddRaskPwa" />).
/// </summary>
internal sealed class RaskPwaState(WebAppManifest manifest)
{
    public WebAppManifest Manifest { get; } = manifest;
}

/// <summary>
///     Emits the PWA wiring directly into the server-rendered <c>&lt;head&gt;</c> as real HTML — the
///     <c>&lt;link rel="manifest"&gt;</c>, an optional <c>&lt;meta name="theme-color"&gt;</c>, and a tiny
///     inline script that registers the service worker. No post-boot JS injection is needed (unlike WASM),
///     and the markup is byte-stable per session, so the live diff codec never emits ops for it. Auto-
///     registering the SW means <c>AddRaskPwa(manifest)</c> is the only call an app needs to be installable.
/// </summary>
internal sealed class RaskPwaHeadContribution(RaskPwaState state) : IRaskHeadContribution
{
    public Component Render()
    {
        var children = new List<Child>
        {
            Components.Link(Rel: "manifest", Href: LiveOptions.PathBase + RaskEndpointExtensions.ManifestPath)
        };

        if (state.Manifest.ThemeColor is { } themeColor)
        {
            children.Add(Components.Meta(Name: "theme-color", Content: themeColor));
        }

        // PathBase is framework-controlled (no untrusted input), so it's safe to inline. register() is
        // idempotent — a re-insert during a head morph just resolves the existing registration.
        var swUrl = LiveOptions.PathBase + RaskEndpointExtensions.ServiceWorkerPath;
        children.Add(Components.Script()[Components.Raw(
            "if(\"serviceWorker\" in navigator){navigator.serviceWorker.register(\""
            + swUrl + "\").catch(function(){});}")]);

        return Components.Fragment()[children];
    }
}
