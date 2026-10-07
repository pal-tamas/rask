using System.Globalization;
using System.Text;
using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's Web Serial API from Rask.Web — talk to a serial device (Arduino / microcontroller, USB-to-serial
///     adapter) from C# in the browser: pick a port from a gesture, write a line, and watch inbound bytes stream into
///     the log. WASM-only: <c>requestPort()</c> needs a live user gesture, and it's Chromium-family only at the time
///     of writing.
/// </summary>
public sealed partial class SerialDemo : Component
{
    private Types.SerialPort? _port;
    private Types.ReadableStreamDefaultReader? _reader;
    private Task _reading = Task.CompletedTask;
    private IAsyncDisposable? _unplugged;
    private int _baudRate = 9600;
    private string _outgoing = string.Empty;
    private readonly List<string> _log = [];
    private string _status = "(idle)";

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Input.Value(_baudRate.ToString(CultureInfo.InvariantCulture)).Label("Baud")
                        .Id("serial-baud")
                        .Type(InputType.Number)
                        .Disabled(_port is not null)
                        .OnInput(v => _baudRate = int.TryParse(v, CultureInfo.InvariantCulture, out var baud) ? baud : 0),
                    Ui.Button.Primary.Icon(Ui.IconName.CubeTransparent)
                        .Id("serial-connect")
                        .Disabled(_port is not null)
                        .OnClick(Connect)["Connect"],
                    Ui.Button.Red
                        .Id("serial-disconnect")
                        .Disabled(_port is null)
                        .OnClick(Disconnect)["Disconnect"]
                ],
                Div.Class("flex items-stretch gap-2 mb-2")[
                    Ui.Input.Value(_outgoing).Label("Line to send")
                        .Id("serial-outgoing")
                        .Placeholder("Line to send")
                        .Disabled(_port is null)
                        .OnInput(v => _outgoing = v),
                    Ui.Button.Primary
                        .Id("serial-send")
                        .Disabled(_port is null)
                        .OnClick(Send)["Send"]
                ],
                Pre
                    .Class("text-sm bg-slate-900 text-slate-100 rounded p-2 mb-2")
                    .Id("serial-log")
                    .Style("min-height: 6rem; max-height: 12rem; overflow: auto")[
                    _log.Count == 0 ? "(no data yet)" : string.Join("\n", _log)],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("serial-status")[_status]]
            ];

    // navigator.serial.requestPort() shows the chooser (dismissing it rejects), then port.open({ baudRate }).
    private async Task Connect()
    {
        try
        {
            if (!await Navigator.Serial.IsSupported)
            {
                _status = "Web Serial not supported in this browser (Chromium-family only)";
                return;
            }

            _port = await Navigator.Serial.RequestPort();
            await _port.Open(new() { BaudRate = _baudRate });
            _unplugged = await _port.OnDisconnect(async () =>
            {
                await Release();
                _status = "Device disconnected";
            });
            _reader = await _port.Readable.GetReader();
            _reading = ReadLoop(_reader);
            _status = $"Connected at {_baudRate} baud";
        }
        catch (JSException ex)
        {
            await Release();
            _status = "Failed: " + ex.Message;
        }
    }

    // reader.read() resolves with each chunk the device sends, and with done once the reader is cancelled.
    private async Task ReadLoop(Types.ReadableStreamDefaultReader reader)
    {
        try
        {
            while (await reader.Read<byte[]>() is { Done: false } chunk)
            {
                _log.Add(Encoding.UTF8.GetString(chunk.Value));
                if (_log.Count > 100)
                {
                    _log.RemoveRange(0, _log.Count - 100);
                }

                StateHasChanged();
            }
        }
        catch (JSException ex)
        {
            _status = "Read stopped: " + ex.Message;
            StateHasChanged();
        }
    }

    // A writer locks the port's writable stream, so take one per line and let it go again.
    private async Task Send()
    {
        if (_port is null)
        {
            return;
        }

        try
        {
            await using var writer = await _port.Writable.GetWriter();
            try
            {
                await writer.Write(Encoding.UTF8.GetBytes(_outgoing + "\n"));
            }
            finally
            {
                await writer.ReleaseLock();
            }

            _status = "Sent: " + _outgoing;
            _outgoing = string.Empty;
        }
        catch (JSException ex)
        {
            _status = "Send failed: " + ex.Message;
        }
    }

    private async Task Disconnect()
    {
        await Release();
        _status = "Disconnected — port released";
    }

    // Cancel the read (it ends the loop), unlock the stream, then close the port and let every handle go.
    private async Task Release()
    {
        var port = _port;
        var reader = _reader;
        _port = null;
        _reader = null;
        try
        {
            if (reader is not null)
            {
                await reader.Cancel();
                await _reading;
                await reader.ReleaseLock();
            }

            if (port is not null)
            {
                await port.Close();
            }
        }
        catch (JSException)
        {
            // An unplugged port has already closed its streams; there is nothing left to release on its side.
        }

        if (_unplugged is not null)
        {
            await _unplugged.DisposeAsync();
            _unplugged = null;
        }

        if (reader is not null)
        {
            await reader.DisposeAsync();
        }

        if (port is not null)
        {
            await port.DisposeAsync();
        }
    }

    protected override Task OnUnmount() => Release();
}
