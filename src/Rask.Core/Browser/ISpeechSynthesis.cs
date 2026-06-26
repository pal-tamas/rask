using System.Text.Json.Serialization;
using Microsoft.JSInterop;

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

/// <summary>
///     Typed access to speech synthesis / text-to-speech (the SpeechSynthesis API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SpeechSynthesis" />) — speak text
///     aloud, e.g. for accessibility or audible notifications. Works on <b>both transports</b>; inject it
///     through a component constructor and call from an event handler.
/// </summary>
/// <remarks>
///     Speaking is best triggered from a user gesture (browser autoplay policies may otherwise stay
///     silent until the user interacts with the page). Gate on <see cref="IsSupportedAsync" />.
/// </remarks>
public interface ISpeechSynthesis
{
    /// <summary>Whether the browser supports speech synthesis (<c>"speechSynthesis" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Speaks <paramref name="text" /> (<c>speechSynthesis.speak(new SpeechSynthesisUtterance(...))</c>),
    ///     applying <paramref name="options" /> when given. Queues behind anything already speaking.
    /// </summary>
    ValueTask SpeakAsync(string text, SpeechOptions? options = null);

    /// <summary>Stops speaking and clears the queue (<c>speechSynthesis.cancel()</c>).</summary>
    ValueTask CancelAsync();
}

/// <summary>
///     Default <see cref="ISpeechSynthesis" />, backed by the unified <see cref="IJSRuntime" />. Building a
///     <c>SpeechSynthesisUtterance</c> is a constructor <see cref="IJSRuntime" /> can't call, so speaking
///     goes through the framework's <c>__raskApi.speak</c> helper; support/cancel are plain helper calls.
/// </summary>
public sealed class SpeechSynthesis(IJSRuntime js) : ISpeechSynthesis
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskApi.speechSupported");

    /// <inheritdoc />
    public ValueTask SpeakAsync(string text, SpeechOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return js.InvokeVoidAsync("__raskApi.speak", text, options ?? new SpeechOptions());
    }

    /// <inheritdoc />
    public ValueTask CancelAsync() => js.InvokeVoidAsync("__raskApi.cancelSpeech");
}
