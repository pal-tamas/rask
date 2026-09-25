using System.Text.Json.Serialization;

namespace Rask.Wasm.Browser;

/// <summary>
///     Narrows the device chooser. Every field is optional; a set field must match. Null fields are omitted
///     from the request (a serialized <c>null</c> id would coerce to 0 and match nothing).
/// </summary>
/// <param name="VendorId">USB vendor id (e.g. <c>0x2341</c> for Arduino).</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="ClassCode">USB device/interface class code.</param>
/// <param name="SubclassCode">USB subclass code.</param>
/// <param name="ProtocolCode">USB protocol code.</param>
/// <param name="SerialNumber">Device serial number.</param>
public sealed record UsbDeviceFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? VendorId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProductId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ClassCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SubclassCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProtocolCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SerialNumber = null);
