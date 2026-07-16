using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IBattery" /> — read the device charge level and charging state, and subscribe to changes.
///     The watch is opened on mount and disposed on unmount; its handler updates state and calls
///     <c>StateHasChanged()</c> (the sanctioned pattern for an externally-pushed update). Browser support is
///     Chromium-only, so each call is gated on <see cref="IBattery.IsSupportedAsync" />; in the native shell
///     it resolves to a real OS backend.
/// </summary>
public sealed class BatteryDemo(IBattery battery) : Component, IAsyncDisposable
{
    private BatteryStatus? _status;
    private string _state = "(read or watch)";
    private IAsyncDisposable? _watch;
    private bool _started;

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender || _started)
        {
            return;
        }

        _started = true;
        if (!await battery.IsSupportedAsync())
        {
            _state = "not supported on this browser";
            StateHasChanged();
            return;
        }

        _watch = await battery.WatchAsync(s =>
        {
            _status = s;
            _state = "live";
            StateHasChanged();
            return Task.CompletedTask;
        });
    }

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Id: "battery-read", OnClickAsync: Read)[
                        "Read now"]
                ],
                Div(Class: "small text-secondary mb-1")[
                    "Level: ", Code(Id: "battery-level")[_status is { } s ? $"{s.Level * 100:0}%" : "(none)"]],
                Div(Class: "small text-secondary mb-1")[
                    "Charging: ", Code(Id: "battery-charging")[_status is { } c ? (c.Charging ? "yes" : "no") : "(none)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "battery-status")[_state]]
            ]
        ];

    private async Task Read()
    {
        try
        {
            _status = await battery.GetStatusAsync();
            _state = _status is null ? "not supported" : "read";
        }
        catch (Exception ex)
        {
            _state = "failed: " + ex.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
        }
    }
}
