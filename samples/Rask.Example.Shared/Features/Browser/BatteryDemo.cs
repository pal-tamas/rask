using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IBattery" /> — read the device charge level and charging state, and subscribe to changes.
///     The watch is opened on mount and disposed on unmount; its handler updates state and calls
///     <c>StateHasChanged()</c> (the sanctioned pattern for an externally-pushed update). Browser support is
///     Chromium-only, so each call is gated on <see cref="IBattery.IsSupportedAsync" />.
/// </summary>
public sealed partial class BatteryDemo(IBattery battery) : Component, IAsyncDisposable
{
    private BatteryStatus? _status;

    // Two labels, not one, because the two halves of this demo write on their own schedules: the watch
    // pushes whenever the device changes, and the button reports what a one-shot read just returned.
    // Sharing a field made whichever wrote last the visible truth — a push landing after a click replaced
    // "read" with "live" and never put it back, which read as the button having done nothing.
    private string _watchState = "(starting…)";
    private string _readState = "(not read yet)";

    // The level/charging figures ARE shared on purpose: both sources describe the same battery, so the
    // freshest value is the right one to show whichever produced it.
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
            _watchState = "not supported on this browser";
            _readState = "not supported on this browser";
            StateHasChanged();
            return;
        }

        _watch = await battery.WatchAsync(s =>
        {
            _status = s;
            _watchState = "live";
            StateHasChanged();
            return Task.CompletedTask;
        });
    }

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Button.Type("button").Class(Tw.BtnPrimary).Id("battery-read").OnClickAsync(Read)[
                        "Read now"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400 mb-1")[
                    "Level: ", Code.Id("battery-level")[_status is { } s ? $"{s.Level * 100:0}%" : "(none)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400 mb-1")[
                    "Charging: ", Code.Id("battery-charging")[_status is { } c ? (c.Charging ? "yes" : "no") : "(none)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400 mb-1")[
                    "Watch: ", Code.Id("battery-watch")[_watchState]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("battery-status")[_readState]]
            ]
        ];

    private async Task Read()
    {
        try
        {
            _status = await battery.GetStatusAsync();
            _readState = _status is null ? "not supported" : "read";
        }
        catch (Exception ex)
        {
            _readState = "failed: " + ex.Message;
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
