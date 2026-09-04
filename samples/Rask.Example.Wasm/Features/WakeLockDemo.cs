using Rask.Core.Browser;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IWakeLock" /> — keep the screen from dimming/locking, then release it. The lock is
///     auto-released when the page is hidden and re-acquired when it returns; disposing the sentinel
///     (here, toggling off — and on unmount via <see cref="IAsyncDisposable" />) releases it for good.
/// </summary>
public sealed partial class WakeLockDemo(IWakeLock wakeLock) : Component, IAsyncDisposable
{
    private IWakeLockSentinel? _sentinel;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button
                        .Class(_sentinel is null ? $"{Tw.BtnPrimary}" : $"{Tw.BtnDanger}")
                        .Id("wakelock-toggle")
                        .OnClickAsync(Toggle)[
                        _sentinel is null ? "Keep screen awake" : "Release"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("wakelock-status")[_status ?? "(idle)"]]
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
