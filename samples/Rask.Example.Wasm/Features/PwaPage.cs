using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="PwaDemo" /> (<c>INotifications</c>/<c>IWebPush</c>/<c>IBadge</c>).
///     Lives in the WASM host because these APIs are WASM-only; surfaced in the shared sidebar via a
///     host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs), nesting in <see cref="ShowcaseLayout" />.
/// </summary>
[Route("pwa")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class PwaPage : Component
{
    protected override Component? Head => Title()["PWA — Rask"];

    protected override Component? Render() =>
    [
        H1(Class: "h2 mb-1")["PWA — notifications & push"],
        P(Class: "text-secondary")[
            "A live demo of the WASM-only PWA APIs. This site is itself an installable, offline PWA — ",
            "install it from your browser's address bar, then try the buttons below."
        ],
        CodeSample(
            ["PwaDemo.cs"],
            Notes: "Local notifications, Web Push readiness, and the installed-app badge — all WASM-only "
                + "(they need a live user gesture or the installed-PWA instance the Server round-trip can't carry).",
            Result: PwaDemo())
    ];
}
