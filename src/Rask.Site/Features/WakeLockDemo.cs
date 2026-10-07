using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>WakeLock</c> from Rask.Web — keep the screen from dimming/locking, then release it. The browser lets
///     go of a screen lock whenever the page is hidden; Rask.Web asks for it again when the page is visible, so the
///     sentinel holds until <c>Release()</c> (here, toggling off — and on unmount).
/// </summary>
public sealed partial class WakeLockDemo : Component
{
    private Types.WakeLockSentinel? _sentinel;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button
                        .Variant(_sentinel is null ? Ui.ButtonVariant.Primary : Ui.ButtonVariant.Danger)
                        .Id("wakelock-toggle")
                        .OnClick(Toggle)[_sentinel is null ? "Keep screen awake" : "Release"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("wakelock-status")[_status ?? "(idle)"]]
            ];

    private async Task Toggle()
    {
        try
        {
            if (_sentinel is not null)
            {
                await Release();
                _status = "Released — the screen can sleep again";
                return;
            }

            if (!await Navigator.WakeLock.IsSupported)
            {
                _status = "Wake lock not supported in this browser";
                return;
            }

            _sentinel = await Navigator.WakeLock.Request(Types.WakeLockType.Screen);
            _status = "Held — the screen will stay awake";
        }
        catch (JSException ex)
        {
            // Refused: a page that is not visible, a battery saver, or a permissions policy.
            _status = "Failed: " + ex.Message;
        }
    }

    // Release() is what lets the screen sleep; disposing only lets go of the handle.
    private async Task Release()
    {
        if (_sentinel is null)
        {
            return;
        }

        await _sentinel.Release();
        await _sentinel.DisposeAsync();
        _sentinel = null;
    }

    protected override Task OnUnmount() => Release();
}
