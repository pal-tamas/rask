using Rask.Core.Routing;
using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     A live, WASM-only demo of the Screen Wake Lock API (<see cref="IWakeLock" />) — hold the screen
///     awake, then release it. WASM-only (the lock is tied to the live document), so it lives in the WASM
///     host and is surfaced via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("wake-lock")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class WakeLockDemo(IWakeLock wakeLock) : Component, IAsyncDisposable
{
    private IWakeLockSentinel? _sentinel;
    private string? _status;

    protected override RenderResult Head => Title()["Wake lock — Rask"];

    protected override RenderResult Render() =>
    [
        H1(Class: "h2 mb-1")["Wake lock"],
        P(Class: "text-secondary")[
            "Keep the screen from dimming or locking via IWakeLock (the Screen Wake Lock API) — for timers, ",
            "reading, or media. The lock is released automatically when the page is hidden and re-acquired ",
            "when it returns."
        ],
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(
                        Class: _sentinel is null ? "btn btn-primary btn-sm" : "btn btn-danger btn-sm",
                        Id: "wakelock-toggle", OnClickAsync: Toggle)[
                        _sentinel is null ? "Keep screen awake" : "Release"]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "wakelock-status")[_status ?? "(idle)"]]
            ]
        ]
    ];

    private async Task Toggle()
    {
        try
        {
            if (_sentinel is not null)
            {
                await _sentinel.DisposeAsync();
                _sentinel = null;
                _status = "Released — the screen can sleep again";
                return;
            }

            if (!await wakeLock.IsSupportedAsync())
            {
                _status = "Wake lock not supported in this browser";
                return;
            }

            _sentinel = await wakeLock.RequestAsync();
            _status = "Held — the screen will stay awake";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_sentinel is not null)
        {
            await _sentinel.DisposeAsync();
            _sentinel = null;
        }
    }
}
