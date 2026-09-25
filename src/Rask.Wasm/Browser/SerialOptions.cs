namespace Rask.Wasm.Browser;

/// <summary>
///     How to open a serial port — passed to <c>SerialPort.open</c>. Defaults match the most common
///     8-N-1 / 9600-baud configuration used by Arduino-style boards.
/// </summary>
/// <param name="BaudRate">Bits per second (e.g. 9600, 115200). Must match the device.</param>
/// <param name="DataBits">Data bits per frame — 7 or 8.</param>
/// <param name="StopBits">Stop bits per frame — 1 or 2.</param>
/// <param name="Parity">Parity checking — <c>"none"</c>, <c>"even"</c>, or <c>"odd"</c>.</param>
/// <param name="BufferSize">Read/write buffer size in bytes.</param>
/// <param name="FlowControl">Flow control — <c>"none"</c> or <c>"hardware"</c>.</param>
/// <param name="Filters">
///     Optional device filters for the port chooser; when set, only matching devices are offered.
/// </param>
public sealed record SerialOptions(
    int BaudRate = 9600,
    int DataBits = 8,
    int StopBits = 1,
    string Parity = "none",
    int BufferSize = 255,
    string FlowControl = "none",
    IReadOnlyList<SerialPortFilter>? Filters = null);
