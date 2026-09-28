namespace Rask.Wasm.Browser;

/// <summary>Wire shape returned by the JS helper: the minted id plus the device descriptor info.</summary>
internal sealed record HidDeviceHandshake(int Id, HidDeviceInfo Info);
