using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
namespace Rask.Server;

/// <summary>
///     Emits the PWA wiring directly into the server-rendered <c>&lt;head&gt;</c> as real HTML — the
///     <c>&lt;link rel="manifest"&gt;</c>, an optional <c>&lt;meta name="theme-color"&gt;</c>, and a tiny
///     inline script that registers the service worker. No post-boot JS injection is needed (unlike WASM),
///     and the markup is byte-stable per session, so the live diff codec never emits ops for it. Auto-
///     registering the SW means <c>AddRaskPwa(manifest)</c> is the only call an app needs to be installable.
/// </summary>
internal sealed partial class RaskPwaHeadContribution(RaskPwaState state) : global::Rask.Core.RaskMarkup, IRaskHeadContribution
{
    public Component Render()
    {
        var children = new List<Component>
        {
            Link.Rel("manifest").Href(LiveOptions.PathBase + RaskEndpointExtensions.ManifestPath)
        };

        if (state.Manifest.ThemeColor is { } themeColor)
        {
            children.Add(Meta.Name("theme-color").Content(themeColor));
        }

        // PathBase is framework-controlled (no untrusted input), so it's safe to inline. register() is
        // idempotent — a re-insert during a head morph just resolves the existing registration.
        var swUrl = LiveOptions.PathBase + RaskEndpointExtensions.ServiceWorkerPath;
        children.Add(Script[Raw
            .Value("if(\"serviceWorker\" in navigator){navigator.serviceWorker.register(\""
            + swUrl + "\").catch(function(){});}")]);

        return [.. children];
    }
}
