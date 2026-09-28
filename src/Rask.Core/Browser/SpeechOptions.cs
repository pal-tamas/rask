using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Options for an utterance (a <c>SpeechSynthesisUtterance</c>,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesisUtterance" />). Unset
///     members take the browser default.
/// </summary>
public sealed record SpeechOptions
{
    /// <summary>BCP-47 language tag for the utterance (e.g. <c>en-US</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lang { get; init; }

    /// <summary>Speaking rate, <c>0.1</c>–<c>10</c> (default <c>1</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Rate { get; init; }

    /// <summary>Pitch, <c>0</c>–<c>2</c> (default <c>1</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Pitch { get; init; }

    /// <summary>Volume, <c>0</c>–<c>1</c> (default <c>1</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Volume { get; init; }
}
