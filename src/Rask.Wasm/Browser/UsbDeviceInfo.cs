namespace Rask.Wasm.Browser;

/// <summary>Descriptor info for a USB device — what the device reports about itself.</summary>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="ManufacturerName">Manufacturer string, if the device exposes one.</param>
/// <param name="ProductName">Product string, if the device exposes one.</param>
/// <param name="SerialNumber">Serial number string, if the device exposes one.</param>
public sealed record UsbDeviceInfo(
    int VendorId, int ProductId, string? ManufacturerName, string? ProductName, string? SerialNumber);
