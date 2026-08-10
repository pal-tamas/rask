using System.Text;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="ISerial" /> — talk to a serial device (Arduino / microcontroller, USB-to-serial adapter)
///     from C# in the browser: pick a port from a gesture, write a line, and watch inbound bytes stream into
///     the log. WASM-only: <c>requestPort()</c> needs a live user gesture and the live port stream, and it's
///     Chromium-family only at the time of writing.
/// </summary>
public sealed partial class SerialDemo(ISerial serial) : Component, IAsyncDisposable
{
    private ISerialPort? _port;
    private int _baudRate = 9600;
    private string _outgoing = string.Empty;
    private readonly List<string> _log = [];
    private string _status = "(idle)";

    protected override Component? Render() =>
        Div.Class("card shadow-sm border-0")[
            Div.Class("card-body")[
                Div.Class("d-flex gap-2 flex-wrap align-items-center mb-2")[
                    Label.Class("small text-secondary mb-0").For("serial-baud")["Baud"],
                    Input<string>()
                        .Id("serial-baud")
                        .Type(InputType.Number)
                        .Class("form-control form-control-sm")
                        .Style("width: 7rem")
                        .Value(_baudRate.ToString())
                        .Disabled(_port is not null)
                        .OnInput(v => int.TryParse(v, out _baudRate)),
                    Button
                        .Class("btn btn-primary btn-sm")
                        .Id("serial-connect")
                        .Disabled(_port is not null)
                        .OnClickAsync(Connect)[I.Class("bi bi-usb-plug me-1"), "Connect"],
                    Button
                        .Class("btn btn-outline-danger btn-sm")
                        .Id("serial-disconnect")
                        .Disabled(_port is null)
                        .OnClickAsync(Disconnect)["Disconnect"]
                ],
                Div.Class("input-group input-group-sm mb-2")[
                    Input<string>()
                        .Id("serial-outgoing")
                        .Class("form-control")
                        .Value(_outgoing)
                        .Placeholder("Line to send")
                        .Disabled(_port is null)
                        .OnInput(v => _outgoing = v),
                    Button
                        .Class("btn btn-primary")
                        .Id("serial-send")
                        .Disabled(_port is null)
                        .OnClickAsync(Send)["Send"]
                ],
                Pre
                    .Class("small bg-dark text-light rounded p-2 mb-2")
                    .Id("serial-log")
                    .Style("min-height: 6rem; max-height: 12rem; overflow: auto")[
                    _log.Count == 0 ? "(no data yet)" : string.Join("\n", _log)],
                Div.Class("small text-secondary")["Status: ", Code.Id("serial-status")[_status]]
            ]
        ];

    private async Task Connect()
    {
        try
        {
            if (!await serial.IsSupportedAsync())
            {
                _status = "Web Serial not supported in this browser (Chromium-family only)";
                return;
            }

            _port = await serial.RequestPortAsync(new SerialOptions(BaudRate: _baudRate), OnData, OnClosed);
            _status = _port is null ? "No port selected" : $"Connected at {_baudRate} baud";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private Task OnData(byte[] data)
    {
        _log.Add(Encoding.UTF8.GetString(data));
        if (_log.Count > 100)
        {
            _log.RemoveRange(0, _log.Count - 100);
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnClosed()
    {
        _port = null;
        _status = "Device disconnected";
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task Send()
    {
        if (_port is null)
        {
            return;
        }

        try
        {
            await _port.WriteAsync(Encoding.UTF8.GetBytes(_outgoing + "\n"));
            _status = "Sent: " + _outgoing;
            _outgoing = string.Empty;
        }
        catch (Exception ex)
        {
            _status = "Send failed: " + ex.Message;
        }
    }

    private async Task Disconnect()
    {
        await CloseInternal();
        _status = "Disconnected — port released";
    }

    private async Task CloseInternal()
    {
        if (_port is not null)
        {
            await _port.DisposeAsync();
            _port = null;
        }
    }

    public async ValueTask DisposeAsync() => await CloseInternal();
}
