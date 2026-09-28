using System.Text.Json.Serialization;

namespace Rask.Wasm.Browser;

/// <summary>
///     What to offer in the device chooser. Set <see cref="Filters" /> (and list any services you'll later
///     access in <see cref="OptionalServices" />), or set <see cref="AcceptAllDevices" /> to show everything
///     (still only services in <see cref="OptionalServices" /> are reachable).
/// </summary>
/// <param name="Filters">Device filters; at least one filter or <paramref name="AcceptAllDevices" /> is required.</param>
/// <param name="OptionalServices">Service UUIDs you intend to access but don't filter on.</param>
/// <param name="AcceptAllDevices">Show every nearby device instead of filtering.</param>
public sealed record BluetoothRequestOptions(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BluetoothFilter>? Filters = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? OptionalServices = null,
    bool AcceptAllDevices = false);
