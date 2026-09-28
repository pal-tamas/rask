namespace Rask.Core.Browser;

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
