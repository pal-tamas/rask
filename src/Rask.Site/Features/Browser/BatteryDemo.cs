
namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>BatteryManager</c>, from Rask.Web — read the device charge level and charging state, and watch them
///     change. Chromium only: elsewhere <c>navigator.getBattery()</c> is missing and the call fails.
/// </summary>
public sealed partial class BatteryDemo : Component
{
    private Types.BatteryManager? _battery;
    private IAsyncDisposable? _levelWatch;
    private IAsyncDisposable? _chargingWatch;
    private double? _level;
    private bool? _charging;

    // Two labels, because the watch and the button write on their own schedules: one shared label let a change event
    // landing after a click replace "read" with "live", which read as the button having done nothing.
    private string _watchState = "(starting…)";
    private string _readState = "(not read yet)";

    protected override async Task OnFirstRender()
    {
        try
        {
            _battery ??= await Navigator.GetBattery();
            _levelWatch = await _battery.OnLevelChange(Refresh);
            _chargingWatch = await _battery.OnChargingChange(Refresh);
            await Refresh();
            _watchState = "live";
        }
        catch (Exception)
        {
            _watchState = "not supported on this browser";
        }
    }

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Id("battery-read").OnClick(Read)["Read now"]
                ],
                Div.Class("text-sm text-ui-muted mb-1")[
                    "Level: ", Code.Id("battery-level")[_level is { } l ? $"{l * 100:0}%" : "(none)"]],
                Div.Class("text-sm text-ui-muted mb-1")[
                    "Charging: ", Code.Id("battery-charging")[_charging switch { null => "(none)", true => "yes", _ => "no" }]],
                Div.Class("text-sm text-ui-muted mb-1")[
                    "Watch: ", Code.Id("battery-watch")[_watchState]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("battery-status")[_readState]]
            ];

    private async Task Refresh()
    {
        if (_battery is not null)
        {
            (_level, _charging) = (await _battery.Level, await _battery.Charging);
        }
    }

    private async Task Read()
    {
        try
        {
            _battery ??= await Navigator.GetBattery();
            await Refresh();
            _readState = "read";
        }
        catch (Exception ex)
        {
            _readState = "failed: " + ex.Message;
        }
    }

    protected override async Task OnUnmount()
    {
        if (_levelWatch is not null)
        {
            await _levelWatch.DisposeAsync();
        }

        if (_chargingWatch is not null)
        {
            await _chargingWatch.DisposeAsync();
        }

        if (_battery is not null)
        {
            await _battery.DisposeAsync();
        }
    }
}
