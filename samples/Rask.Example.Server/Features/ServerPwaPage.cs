using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Server.Features;

/// <summary>
///     Server-host showcase page for <see cref="ServerPwaDemo" /> (<c>INotifications</c>/<c>IWebPush</c>/
///     <c>IBadge</c>). These PWA APIs are transport-agnostic, so the Server host runs them over its live
///     WebSocket; surfaced in the shared sidebar via a host-registered <see cref="ShowcaseNavEntry" />
///     (see Program.cs), nesting in <see cref="ShowcaseLayout" />.
/// </summary>
[Route("server-pwa")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class ServerPwaPage : Component
{
    protected override Component? Head => Title()["Server PWA — Rask"];

    protected override Component? Render() =>
    [
        H1(Class: "h2 mb-1")["Server PWA — notifications & push"],
        P(Class: "text-secondary")[
            "A live demo of PWA APIs running on the ", Strong()["Server"],
            " host (server-rendered, driven over the WebSocket). This site is an installable PWA — install ",
            "it from your browser's address bar, then try the buttons below. Note it is installable and ",
            "push-capable, but not an offline app: offline navigations show a static offline page."
        ],
        CodeSample(
            ["ServerPwaDemo.cs"],
            Notes: "Local notifications, the full Web Push subscribe→send loop (via Rask.WebPush), and the "
                + "installed-app badge — the same transport-agnostic APIs the WASM showcase uses, here on Server.",
            Result: ServerPwaDemo())
    ];
}
