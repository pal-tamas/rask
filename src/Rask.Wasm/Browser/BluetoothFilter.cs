using System.Text.Json.Serialization;

namespace Rask.Wasm.Browser;

/// <summary>
///     One entry in a <see cref="BluetoothRequestOptions" /> filter list — a device must match every set field.
/// </summary>
/// <param name="Services">Required advertised GATT service UUIDs (name, e.g. <c>"battery_service"</c>, or full UUID).</param>
/// <param name="Name">Exact advertised device name.</param>
/// <param name="NamePrefix">Advertised-name prefix.</param>
public sealed record BluetoothFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Services = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NamePrefix = null);
