namespace Rask.Wasm.Browser;

/// <summary>Wire shape returned by the JS helper for a device: the minted id plus its identity.</summary>
internal sealed record BluetoothDeviceHandshake(int Id, BluetoothDeviceInfo Info);
