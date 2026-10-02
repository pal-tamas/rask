using System.Globalization;
using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>navigator.getGamepads()</c> from Rask.Web — read connected game controllers and react to stick/button
///     input. MDN has no input event (a game polls every frame), so this demo reads the pads when one connects or
///     disconnects, and polls ten times a second while one is connected, re-rendering only when a reading changes.
/// </summary>
public sealed partial class GamepadDemo : Component
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private List<Pad> _pads = [];
    private IAsyncDisposable? _connected;
    private IAsyncDisposable? _disconnected;
    private Task _polling = Task.CompletedTask;
    private bool _unmounted;
    private string _status = "(idle)";

    protected override async Task OnFirstRender()
    {
        if (_connected is not null)
        {
            return;
        }

        _connected = await Window.OnGamepadConnected(Changed);
        _disconnected = await Window.OnGamepadDisconnected(Changed);
        _status = "Ready — connect a controller and press a button";
        await Changed();
    }

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("text-sm text-ui-muted mb-2")["Status: ", Code.Id("gamepad-status")[_status]],
                Div.Class("text-sm text-ui-muted mb-2")[
                    "Connected pads: ", Code.Id("gamepad-count")[_pads.Count]],
                _pads.Count == 0
                    ? Div.Class("text-ui-muted text-sm")["No controllers connected."]
                    : Ui.List[
                        _pads.Select(p => Li.Key(p.Index)[
                            Div.Class("text-sm font-semibold")[$"#{p.Index} — {p.Id}"],
                            Div.Class("text-sm text-ui-muted")[$"axes [{p.Axes}] · buttons pressed {p.Pressed}/{p.Buttons}"]
                        ])
                    ]
            ];

    private async Task Changed()
    {
        await Read();
        if (_pads.Count > 0 && _polling.IsCompleted)
        {
            _polling = Poll();
        }
    }

    // A pad only reports its state when asked, so ask every tick while one is connected.
    private async Task Poll()
    {
        while (!_unmounted && _pads.Count > 0)
        {
            await Task.Delay(Tick);
            if (await Read())
            {
                StateHasChanged();
            }
        }
    }

    // navigator.getGamepads(): one kept Gamepad per slot, null where none is connected. Read each, then let it go.
    private async Task<bool> Read()
    {
        try
        {
            List<Pad> pads = [];
            foreach (var gamepad in await Navigator.GetGamepads())
            {
                if (gamepad is null)
                {
                    continue;
                }

                await using (gamepad)
                {
                    var buttons = await gamepad.Buttons;
                    pads.Add(new Pad(
                        await gamepad.Index,
                        await gamepad.Id,
                        string.Join(", ", (await gamepad.Axes).Select(a => a.ToString("0.00", CultureInfo.InvariantCulture))),
                        buttons.Count(b => b.Pressed),
                        buttons.Length));
                }
            }

            var changed = !pads.SequenceEqual(_pads);
            _pads = pads;
            return changed;
        }
        catch (JSException ex)
        {
            _status = "Gamepad API not supported: " + ex.Message;
            _pads = [];
            return true;
        }
    }

    protected override async Task OnUnmount()
    {
        _unmounted = true;
        if (_connected is not null)
        {
            await _connected.DisposeAsync();
        }

        if (_disconnected is not null)
        {
            await _disconnected.DisposeAsync();
        }
    }

    private sealed record Pad(int Index, string Id, string Axes, int Pressed, int Buttons);
}
