namespace Rask.Wasm.Browser;

/// <summary>One media input/output device from <c>navigator.mediaDevices.enumerateDevices</c>.</summary>
/// <param name="DeviceId">Stable id to pin a specific device (empty until permission is granted).</param>
/// <param name="Kind"><c>"audioinput"</c>, <c>"videoinput"</c>, or <c>"audiooutput"</c>.</param>
/// <param name="Label">Human-readable name (empty until permission is granted).</param>
/// <param name="GroupId">Groups devices belonging to the same physical hardware.</param>
public sealed record MediaDeviceInfo(string DeviceId, string Kind, string Label, string GroupId);
