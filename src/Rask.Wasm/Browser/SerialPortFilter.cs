using System.Text.Json.Serialization;

namespace Rask.Wasm.Browser;

/// <summary>Narrows the port chooser to a specific USB device by vendor/product id.</summary>
/// <param name="UsbVendorId">USB vendor id (e.g. <c>0x2341</c> for Arduino). Null to not filter on it.</param>
/// <param name="UsbProductId">USB product id. Null to not filter on it.</param>
public sealed record SerialPortFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsbVendorId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsbProductId = null);
