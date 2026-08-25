using System.Text.Json;
using System.Text.Json.Serialization;
using Rask.Core.Browser;

namespace Rask.Native;

/// <summary>
///     Source-generated JSON metadata for everything the capability bridge puts on the wire.
/// </summary>
/// <remarks>
///     Its own context rather than an addition to <c>RaskBrowserJsonContext</c>, for the same reason
///     <c>NativeChromeJsonContext</c> is its own: the iOS head is full-AOT, and a context that lives beside
///     the code serializing through it is one that cannot be trimmed away from under it. Web defaults so the
///     payloads match the shapes the page's JS wrappers already produce and read.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(NativeCapabilityRequest))]
[JsonSerializable(typeof(NativeCapabilityReply))]
[JsonSerializable(typeof(SpeakRequest))]
[JsonSerializable(typeof(ShowNotificationRequest))]
[JsonSerializable(typeof(ShareData))]
[JsonSerializable(typeof(GeolocationOptions))]
[JsonSerializable(typeof(GeolocationPosition))]
[JsonSerializable(typeof(NetworkStatus))]
[JsonSerializable(typeof(BatteryStatus))]
[JsonSerializable(typeof(ScreenInfo))]
[JsonSerializable(typeof(SpeechOptions))]
[JsonSerializable(typeof(NotificationOptions))]
[JsonSerializable(typeof(NativeCapabilityEvent))]
[JsonSerializable(typeof(WatchRequest))]
[JsonSerializable(typeof(GeolocationWatchRequest))]
[JsonSerializable(typeof(SpeechStartRequest))]
[JsonSerializable(typeof(SpeechRecognitionOptions))]
[JsonSerializable(typeof(RecognitionResult))]
[JsonSerializable(typeof(OrientationReading))]
[JsonSerializable(typeof(MotionReading))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(int?))]
internal sealed partial class NativeCapabilityJsonContext : JsonSerializerContext;

/// <summary>
///     One capability invoke from the page. <see cref="Id" /> is the page's correlation id — the bridge
///     echoes it back so a reply reaches the promise that is waiting for it, exactly as
///     <c>jsResult</c> / <c>dotNetInvoke</c> already do in the other direction.
/// </summary>
/// <param name="Type">Always <c>capability</c>; the head routes on it.</param>
/// <param name="Id">The page's correlation id, or null for a call whose result nobody awaits.</param>
/// <param name="Component">The capability name, e.g. <c>geolocation</c>.</param>
/// <param name="Op">The operation on it, e.g. <c>getCurrentPosition</c>.</param>
/// <param name="Data">The argument payload as JSON, or null.</param>
internal sealed record NativeCapabilityRequest(
    string? Type, string? Id, string? Component, string? Op, string? Data);

/// <summary>
///     The answer to a <see cref="NativeCapabilityRequest" />, delivered to
///     <c>window.__raskNative.capabilityResult</c>.
/// </summary>
/// <param name="Id">The correlation id being answered.</param>
/// <param name="Success">Whether the op ran. False carries <paramref name="Error" /> instead of a result.</param>
/// <param name="Result">The op's result as JSON, or null for an op that returns nothing.</param>
/// <param name="Error">
///     Why it failed. A message rather than silence on purpose: an unknown capability used to be consumed as
///     a no-op, which left the page's await pending for ever — the worst way to report "there is nothing
///     here".
/// </param>
internal sealed record NativeCapabilityReply(string Id, bool Success, string? Result, string? Error);

/// <summary>
///     One reading from a live subscription, delivered to <c>window.__raskNative.capabilityEvent</c>.
/// </summary>
/// <param name="Sub">The subscription id the page chose when it started the stream.</param>
/// <param name="Payload">The reading as JSON, in the shape the page's own JS wrapper would have produced.</param>
internal sealed record NativeCapabilityEvent(string Sub, string? Payload);

/// <summary>Starting a stream that takes no options.</summary>
/// <param name="Sub">The id the page will use to route readings and later release the handle.</param>
internal sealed record WatchRequest(string Sub);

/// <summary>Starting a geolocation watch.</summary>
internal sealed record GeolocationWatchRequest(string Sub, GeolocationOptions? Options);

/// <summary>Starting speech recognition.</summary>
internal sealed record SpeechStartRequest(string Sub, SpeechRecognitionOptions? Options);
