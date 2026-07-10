using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Payload for the OS share sheet / Web Share API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Navigator/share" />). At least one
///     field should be set; <c>null</c> fields are omitted from the share.
/// </summary>
/// <remarks>
///     The shared payload for both ways to share: the all-host headless <c>Shareable</c> component (which
///     fires <c>navigator.share</c> client-side inside the click gesture, so it works on every host including
///     Server) and the in-process imperative <c>IShare</c> in <c>Rask.Client.Browser</c> (call it from any
///     handler on the WASM / Native hosts). Lives in <c>Rask.Core.Browser</c> because both paths — and every
///     host — use it.
/// </remarks>
public sealed record ShareData
{
    /// <summary>Title of the shared content.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    /// <summary>Body text to share.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>URL to share.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }
}
