using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>Options for a speech-recognition session. Unset members take the platform default.</summary>
public sealed record SpeechRecognitionOptions
{
    /// <summary>BCP-47 language tag to recognise (e.g. <c>en-US</c>). Defaults to the page/device language.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lang { get; init; }

    /// <summary>
    ///     Keep listening and emitting results until the session is disposed. When <c>false</c> (the default)
    ///     recognition stops after the first utterance.
    /// </summary>
    public bool Continuous { get; init; }

    /// <summary>
    ///     Also emit interim (not-yet-final) hypotheses as the user speaks, not only the final transcript.
    /// </summary>
    public bool InterimResults { get; init; }
}
