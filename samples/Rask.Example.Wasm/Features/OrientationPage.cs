using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="OrientationDemo" /> (<c>IScreenOrientation</c>). Surfaced in
///     the shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("orientation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class OrientationPage : Component
{
    protected override Component? Head => Title()["Orientation — Rask"];

    protected override Component? Render() =>
    [
        H1(Class: "h2 mb-1")["Orientation"],
        P(Class: "text-secondary")[
            "Read the screen orientation via IScreenOrientation and, for an installed or fullscreen app, ",
            "lock it. Locking is usually rejected outside fullscreen and is often unsupported on desktop."
        ],
        CodeSample(
            ["OrientationDemo.cs"],
            Notes: "GetAsync returns the OrientationInfo (type + angle); LockAsync/UnlockAsync change it. "
                + "WASM-only — locking needs the live, usually fullscreen, document.",
            Result: OrientationDemo())
    ];
}
