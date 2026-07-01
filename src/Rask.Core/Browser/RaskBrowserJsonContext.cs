using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Source-generated JSON metadata for the framework's own browser-API types, so they deserialize
///     from <see cref="Microsoft.JSInterop.IJSRuntime" /> results without reflection. The WASM runtime
///     inserts <see cref="Default" /> ahead of its reflection fallback, keeping these types trim-safe in
///     a <c>PublishTrimmed</c> app (where unrooted reflective members would otherwise be removed).
///     Web defaults (camelCase, case-insensitive) match both the JS payload shape and the JSInterop
///     serializer options.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GeolocationPosition))]
[JsonSerializable(typeof(PushSubscription))]
[JsonSerializable(typeof(NotificationOptions))]
[JsonSerializable(typeof(NetworkReading))]
[JsonSerializable(typeof(SpeechOptions))]
[JsonSerializable(typeof(ScreenInfo))]
[JsonSerializable(typeof(StorageEstimate))]
[JsonSerializable(typeof(VisualViewport))]
[JsonSerializable(typeof(IntersectionEntry))]
[JsonSerializable(typeof(ResizeEntry))]
[JsonSerializable(typeof(MutationEntry))]
[JsonSerializable(typeof(MediaMetadata))]
[JsonSerializable(typeof(GamepadReading))]
[JsonSerializable(typeof(OrientationReading))]
[JsonSerializable(typeof(MotionReading))]
[JsonSerializable(typeof(NavigationTiming))]
[JsonSerializable(typeof(PublicKeyCredentialCreationOptions))]
[JsonSerializable(typeof(PublicKeyCredentialRequestOptions))]
[JsonSerializable(typeof(AttestationResult))]
[JsonSerializable(typeof(AssertionResult))]
[JsonSerializable(typeof(FilePickerOptions))]
[JsonSerializable(typeof(SaveFilePickerOptions))]
[JsonSerializable(typeof(FileSystemHandleInfo))]
[JsonSerializable(typeof(FileSystemHandleInfo[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
internal sealed partial class RaskBrowserJsonContext : JsonSerializerContext;
