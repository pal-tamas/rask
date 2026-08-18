using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="WakeLockDemo" /> (<c>IWakeLock</c>). Lives in the WASM host
///     (the API is WASM-only) and is surfaced in the shared sidebar via a host-registered
///     <see cref="ShowcaseNavEntry" /> (see Program.cs), nesting in the shared <see cref="ShowcaseLayout" />.
/// </summary>
[Route("wake-lock")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class WakeLockPage : Component
{
    protected override Component? HeadAssets => Title["Wake lock — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["Wake lock"],
        P.Class("text-secondary")[
            "Keep the screen from dimming or locking via IWakeLock (the Screen Wake Lock API) — for timers, ",
            "reading, or media. The lock is released automatically when the page is hidden and re-acquired ",
            "when it returns."
        ],
        CodeSample
            .Files(["WakeLockDemo.cs"])
            .Notes("RequestAsync returns an IWakeLockSentinel (IAsyncDisposable); dispose it to release. "
                + "WASM-only — the lock is tied to the live document.")
            .Result(WakeLockDemo)
    ];
}
