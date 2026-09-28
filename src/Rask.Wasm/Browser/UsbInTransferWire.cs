namespace Rask.Wasm.Browser;

/// <summary>Wire shape for an inbound transfer — <c>Data</c> is base64 (raw byte[] doesn't marshal).</summary>
internal sealed record UsbInTransferWire(string Status, string Data);
