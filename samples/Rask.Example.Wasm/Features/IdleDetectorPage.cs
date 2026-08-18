using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="IdleDetectorDemo" /> (<c>IIdleDetector</c>). Surfaced in the
///     shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("idle")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class IdleDetectorPage : Component
{
    protected override Component? HeadAssets => Title["Idle detection — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["Idle detection"],
        P.Class("text-secondary")[
            "Be notified when the user goes idle (no input for a threshold) or the screen locks, via ",
            "IIdleDetector (the Idle Detection API) — e.g. to auto-lock a session, pause a sync, or update ",
            "presence in a collaborative app. WASM-only: the idle-detection permission needs a live gesture ",
            "and the detector needs the live document."
        ],
        CodeSample
            .Files(["IdleDetectorDemo.cs"])
            .Notes("RequestPermissionAsync() must run from a gesture; WatchAsync(onChange, thresholdSeconds) "
                + "then pushes an IdleReading on each user/screen state change. The spec enforces a 60-second "
                + "minimum threshold. Dispose the handle to stop.")
            .Result(IdleDetectorDemo)
    ];
}
