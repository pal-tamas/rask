namespace Rask.Wasm.Browser;

/// <summary>Descriptor info for a HID device.</summary>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="ProductName">Product string, if the device exposes one.</param>
public sealed record HidDeviceInfo(int VendorId, int ProductId, string? ProductName);
