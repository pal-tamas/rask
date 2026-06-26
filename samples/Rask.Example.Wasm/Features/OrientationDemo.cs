using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IScreenOrientation" /> — read the current screen orientation and, for an installed or
///     fullscreen app, lock/unlock it. Locking is usually rejected outside fullscreen and is often
///     unsupported on desktop, so each call is wrapped in try/catch.
/// </summary>
public sealed class OrientationDemo(IScreenOrientation orientation) : Component
{
    private string? _current;
    private string? _status;

    protected override RenderResult Render() =>
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
