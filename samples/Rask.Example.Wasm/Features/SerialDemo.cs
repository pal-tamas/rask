using System.Text;
using Rask.Example.Shared;
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
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Label.Class("text-sm text-slate-500 dark:text-slate-400 mb-0").For("serial-baud")["Baud"],
                    Input
                        .Value(_baudRate.ToString())
                        .Id("serial-baud")
                        .Type(InputType.Number)
                        .Class(Ui.Input)
                        .Style("width: 7rem")
                        .Disabled(_port is not null)
                        .OnInput(v => int.TryParse(v, out _baudRate)),
                    Button
                        .Class(Ui.BtnPrimary)
                        .Id("serial-connect")
                        .Disabled(_port is not null)
                        .OnClickAsync(Connect)[Icon.Name(IconName.UsbPlug).Class("me-1"), "Connect"],
                    Button
                        .Class(Ui.BtnOutlineDanger)
                        .Id("serial-disconnect")
                        .Disabled(_port is null)
                        .OnClickAsync(Disconnect)["Disconnect"]
                ],
                Div.Class($"{Ui.InputGroup} mb-2")[
                    Input
                        .Value(_outgoing)
                        .Id("serial-outgoing")
                        .Class(Ui.Input)
                        .Placeholder("Line to send")
                        .Disabled(_port is null)
                        .OnInput(v => _outgoing = v),
                    Button
                        .Class(Ui.BtnPrimary)
                        .Id("serial-send")
                        .Disabled(_port is null)
                        .OnClickAsync(Send)["Send"]
                ],
                Pre
                    .Class("text-sm bg-slate-900 text-slate-100 rounded p-2 mb-2")
                    .Id("serial-log")
                    .Style("min-height: 6rem; max-height: 12rem; overflow: auto")[
                    _log.Count == 0 ? "(no data yet)" : string.Join("\n", _log)],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("serial-status")[_status]]
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
