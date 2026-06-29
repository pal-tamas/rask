using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IGamepad" /> — poll connected game controllers and react to stick/button input. The
///     framework runs the <c>requestAnimationFrame</c> poll and pushes a reading only when a pad's state
///     changes; this demo keeps the latest reading per connected pad.
/// </summary>
public sealed class GamepadDemo(IGamepad gamepad) : Component, IAsyncDisposable
{
    private readonly Dictionary<int, GamepadReading> _pads = [];
    private IAsyncDisposable? _watch;
    private string _status = "(idle)";

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender || _watch is not null)
        {
            return;
        }

        if (!await gamepad.IsSupportedAsync())
        {
            _status = "Gamepad API not supported";
            StateHasChanged();
            return;
        }

        _status = "Ready — connect a controller and press a button";
        _watch = await gamepad.WatchAsync(reading =>
        {
            if (reading.Connected)
            {
                _pads[reading.Index] = reading;
            }
            else
            {
                _pads.Remove(reading.Index);
            }

            StateHasChanged();
            return Task.CompletedTask;
        });
        StateHasChanged();
    }

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "small text-secondary mb-2")["Status: ", Code(Id: "gamepad-status")[_status]],
                Div(Class: "small text-secondary mb-2")[
                    "Connected pads: ", Code(Id: "gamepad-count")[_pads.Count.ToString()]],
                _pads.Count == 0
                    ? Div(Class: "text-secondary small")["No controllers connected."]
                    : Ul(Class: "list-group list-group-flush")[
                        _pads.Values.Select(p => (Child)Li(Class: "list-group-item px-0", Key: p.Index)[
                            Div(Class: "small fw-semibold")[$"#{p.Index} — {p.Id}"],
                            Div(Class: "small text-secondary")[
                                $"axes [{string.Join(", ", p.Axes.Select(a => a.ToString("0.00")))}] · "
                                + $"buttons pressed {p.Buttons.Count(b => b > 0.5)}/{p.Buttons.Count}"]
                        ])
                    ]
            ]
        ];

    public async ValueTask DisposeAsync()
    {
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
        }
    }
}
