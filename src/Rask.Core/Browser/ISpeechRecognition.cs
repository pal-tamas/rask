using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

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

/// <summary>One recognition result (a <c>SpeechRecognitionResult</c>).</summary>
/// <param name="Transcript">The recognised text.</param>
/// <param name="IsFinal">Whether this is a final result (<c>true</c>) or an interim hypothesis (<c>false</c>).</param>
/// <param name="Confidence">Recognition confidence, <c>0.0</c>–<c>1.0</c>; <c>0</c> when the platform omits it.</param>
public sealed record RecognitionResult(string Transcript, bool IsFinal, double Confidence);

/// <summary>
///     Typed access to speech recognition / dictation (the SpeechRecognition API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/SpeechRecognition" />) — turn spoken audio
///     into text, e.g. for voice input or hands-free control. The counterpart to <see cref="ISpeechSynthesis" />.
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

/// <summary>
///     Infrastructure for <see cref="ISpeechRecognition" /> — routes a pushed result back to the right C#
///     handler by session id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskSpeechRecognition</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SpeechRecognitionInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<RecognitionResult, Task>> Handlers = new();

    internal static int Register(Func<RecognitionResult, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge for each recognition result; do not call.</summary>
    [JSInvokable("RaskSpeechResult")]
    public static Task Result(int id, RecognitionResult result) =>
        Handlers.TryGetValue(id, out var handler) ? handler(result) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="ISpeechRecognition" />, backed by the unified <see cref="IJSRuntime" />. The
///     framework's <c>__raskSpeechRecognition</c> helper drives <c>webkitSpeechRecognition</c> under the
///     C#-minted id and pushes each result into <see cref="SpeechRecognitionInterop" />.
/// </summary>
public sealed class SpeechRecognition : ISpeechRecognition
{
    private readonly IJSRuntime _js;

    // Root SpeechRecognitionInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Result method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(SpeechRecognitionInterop))]
    public SpeechRecognition(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskSpeechRecognition.isSupported");

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> StartAsync(
        Func<RecognitionResult, Task> onResult, SpeechRecognitionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(onResult);

        // Register before starting so a first result can't race ahead of the handler.
        var id = SpeechRecognitionInterop.Register(onResult);
        try
        {
            await _js.InvokeVoidAsync("__raskSpeechRecognition.start", id, options ?? new SpeechRecognitionOptions());
        }
        catch
        {
            SpeechRecognitionInterop.Unregister(id);
            throw;
        }

        return new Session(_js, id);
    }

    private sealed class Session(IJSRuntime js, int id) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SpeechRecognitionInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskSpeechRecognition.stop", id);
        }
    }
}
