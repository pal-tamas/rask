namespace Rask.Wasm.Browser;

/// <summary>
///     The setup packet for a control transfer (<c>USBControlTransferParameters</c>).
/// </summary>
/// <param name="RequestType"><c>"standard"</c>, <c>"class"</c>, or <c>"vendor"</c>.</param>
/// <param name="Recipient"><c>"device"</c>, <c>"interface"</c>, <c>"endpoint"</c>, or <c>"other"</c>.</param>
/// <param name="Request">The <c>bRequest</c> field.</param>
/// <param name="Value">The <c>wValue</c> field.</param>
/// <param name="Index">The <c>wIndex</c> field.</param>
public sealed record UsbControlTransferParams(
    string RequestType, string Recipient, int Request, int Value, int Index);
