using System.Text.Json.Serialization;

namespace Rask.Wasm.Browser;

/// <summary>
///     Narrows the device chooser. Every field is optional; a set field must match. Null fields are omitted
///     (a serialized <c>null</c> would match nothing).
/// </summary>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="UsagePage">Top-level collection usage page (e.g. <c>0x01</c> generic desktop).</param>
/// <param name="Usage">Top-level collection usage (e.g. <c>0x05</c> game pad).</param>
public sealed record HidDeviceFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? VendorId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProductId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsagePage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Usage = null);
