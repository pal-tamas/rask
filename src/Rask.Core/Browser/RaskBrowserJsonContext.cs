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
[JsonSerializable(typeof(ShareData))]
[JsonSerializable(typeof(Rask.Core.Components.GesturePayload))]
[JsonSerializable(typeof(Rask.Core.Components.GestureMediaConstraints))]
[JsonSerializable(typeof(PublicKeyCredentialCreationOptions))]
[JsonSerializable(typeof(PublicKeyCredentialRequestOptions))]
[JsonSerializable(typeof(AttestationResult))]
[JsonSerializable(typeof(AssertionResult))]
[JsonSerializable(typeof(SignalingJoin))]
[JsonSerializable(typeof(SignalingSignal))]
[JsonSerializable(typeof(RtcConfiguration))]
[JsonSerializable(typeof(RtcDescription))]
[JsonSerializable(typeof(RtcIceCandidate))]
[JsonSerializable(typeof(RtcIceCandidate[]))]
[JsonSerializable(typeof(RtcDataChannelOptions))]
[JsonSerializable(typeof(RtcMessageWire))]
[JsonSerializable(typeof(RtcMessageWire[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
internal sealed partial class RaskBrowserJsonContext : JsonSerializerContext;
