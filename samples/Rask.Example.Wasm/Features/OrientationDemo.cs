using Rask.Core.Routing;
using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     A live, WASM-only demo of the Screen Orientation API (<see cref="IScreenOrientation" />) — read the
///     current orientation, and lock/unlock it. WASM-only (locking needs the live, usually fullscreen,
///     document), so it lives in the WASM host and is surfaced via a host-registered
///     <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("orientation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class OrientationDemo(IScreenOrientation orientation) : Component
{
    private string? _current;
    private string? _status;

    protected override RenderResult Head => Title()["Orientation — Rask"];

    protected override RenderResult Render() =>
    [
        H1(Class: "h2 mb-1")["Orientation"],
        P(Class: "text-secondary")[
            "Read the screen orientation via IScreenOrientation and, for an installed or fullscreen app, ",
            "lock it. Locking is usually rejected outside fullscreen and is often unsupported on desktop."
        ],
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-primary btn-sm", Id: "orientation-read", OnClickAsync: Read)["Read current"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "orientation-portrait",
                        OnClickAsync: () => Lock(OrientationLock.Portrait))["Lock portrait"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "orientation-landscape",
                        OnClickAsync: () => Lock(OrientationLock.Landscape))["Lock landscape"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "orientation-unlock", OnClickAsync: Unlock)[
                        "Unlock"]
                ],
                Div(Class: "small text-secondary mb-1")[
                    "Current: ", Code(Id: "orientation-current")[_current ?? "(read to see)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "orientation-status")[_status ?? "(idle)"]]
            ]
        ]
    ];

    private async Task Read()
    {
        try
        {
            if (!await orientation.IsSupportedAsync())
            {
                _current = "not supported";
                return;
            }

            var info = await orientation.GetAsync();
            _current = $"{info.Type} ({info.Angle}°)";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Lock(OrientationLock to)
    {
        try
        {
            await orientation.LockAsync(to);
            _status = $"Locked to {to}";
            await Read();
        }
        catch (Exception ex)
        {
            _status = $"Lock rejected (needs fullscreen?): {ex.Message}";
        }
    }

    private async Task Unlock()
    {
        try
        {
            await orientation.UnlockAsync();
            _status = "Unlocked";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }
}
