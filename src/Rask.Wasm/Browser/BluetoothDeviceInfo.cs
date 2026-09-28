namespace Rask.Wasm.Browser;

/// <summary>Identity of a Bluetooth device.</summary>
/// <param name="Id">Stable per-origin device id.</param>
/// <param name="Name">Advertised name, if any.</param>
public sealed record BluetoothDeviceInfo(string Id, string? Name);
