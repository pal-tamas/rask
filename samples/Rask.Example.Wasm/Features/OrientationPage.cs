using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="OrientationDemo" /> (<c>IScreenOrientation</c>). Surfaced in
///     the shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("orientation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class OrientationPage : Component
{
    protected override Component? HeadAssets => Title["Orientation — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Orientation"],
        P.Class("text-ui-muted")[
            "Read the screen orientation via IScreenOrientation and, for an installed or fullscreen app, ",
            "lock it. Locking is usually rejected outside fullscreen and is often unsupported on desktop."
        ],
        CodeSample
            .Files(["OrientationDemo.cs"])
            .Notes("GetAsync returns the OrientationInfo (type + angle); LockAsync/UnlockAsync change it. "
                + "WASM-only — locking needs the live, usually fullscreen, document.")
            .Result(OrientationDemo)
    ];
}
