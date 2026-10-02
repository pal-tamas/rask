namespace Rask.Core.Browser;

/// <summary>
///     Typed access to speech recognition / dictation (the SpeechRecognition API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SpeechRecognition" />) — turn spoken audio
///     into text, e.g. for voice input or hands-free control. The counterpart to <c>SpeechSynthesis</c>.
///     Works on <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         Call <see cref="StartAsync" /> from a user gesture (it prompts for microphone access); the platform
///         <b>pushes</b> each result to the callback (via a static <c>[JSInvokable]</c>, so one wiring serves
///         both transports). Dispose the returned handle to stop listening and release the microphone. A
///         handler that updates state should call <c>StateHasChanged()</c> (it's a subscription, not a
///         render/binding callback, so RASK026 doesn't apply).
///     </para>
///     <para>
///         Browser support is Chromium-family (as <c>webkitSpeechRecognition</c>); gate on
///         <see cref="IsSupportedAsync" />. Recognition needs microphone permission on every platform.
///     </para>
/// </remarks>
public interface ISpeechRecognition
{
    /// <summary>Whether the platform supports speech recognition (<c>"webkitSpeechRecognition" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Starts listening and delivers each <see cref="RecognitionResult" /> to <paramref name="onResult" />.
    ///     Returns a handle; dispose it to stop and release the microphone. Must be called from a user gesture.
    /// </summary>
    ValueTask<IAsyncDisposable> StartAsync(
        Func<RecognitionResult, Task> onResult, SpeechRecognitionOptions? options = null);
}
