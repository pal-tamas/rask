namespace Rask.Wasm.Browser;

/// <summary>Result of an outbound transfer.</summary>
/// <param name="Status"><c>"ok"</c> or <c>"stall"</c>.</param>
/// <param name="BytesWritten">How many bytes the device accepted.</param>
public sealed record UsbOutTransferResult(string Status, int BytesWritten);
