using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Wasm.Browser;

/// <summary>
///     Source-generated JSON metadata for WASM-only browser-API types (those that can't run on the
///     Server transport), so they serialize through <see cref="Microsoft.JSInterop.IJSRuntime" /> without
///     reflection. <see cref="WasmJSRuntime" /> inserts <see cref="Default" /> ahead of its reflection
///     fallback, keeping these types trim-safe in a <c>PublishTrimmed</c> app. The shared (both-transport)
///     types live in <c>Rask.Core.Browser.RaskBrowserJsonContext</c>.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ShareData))]
[JsonSerializable(typeof(PushSubscription))]
[JsonSerializable(typeof(NotificationOptions))]
[JsonSerializable(typeof(OrientationReading))]
[JsonSerializable(typeof(MediaConstraints))]
[JsonSerializable(typeof(MediaDeviceInfo))]
[JsonSerializable(typeof(MediaDeviceInfo[]))]
[JsonSerializable(typeof(IdleReading))]
[JsonSerializable(typeof(SerialOptions))]
[JsonSerializable(typeof(SerialPortFilter))]
[JsonSerializable(typeof(SerialPortFilter[]))]
[JsonSerializable(typeof(UsbDeviceFilter))]
[JsonSerializable(typeof(UsbDeviceFilter[]))]
[JsonSerializable(typeof(UsbDeviceInfo))]
[JsonSerializable(typeof(UsbDeviceHandshake))]
[JsonSerializable(typeof(UsbDeviceHandshake[]))]
[JsonSerializable(typeof(UsbControlTransferParams))]
[JsonSerializable(typeof(UsbInTransferWire))]
[JsonSerializable(typeof(UsbOutTransferResult))]
internal sealed partial class RaskWasmBrowserJsonContext : JsonSerializerContext;
