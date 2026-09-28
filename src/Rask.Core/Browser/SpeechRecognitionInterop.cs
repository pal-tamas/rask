using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="ISpeechRecognition" /> — routes a pushed result back to the right C#
///     handler by session id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskSpeechRecognition</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SpeechRecognitionInterop
{
    private static readonly JsCallbacks<Func<RecognitionResult, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<RecognitionResult, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge for each recognition result; do not call.</summary>
    [JSInvokable("RaskSpeechResult")]
    public static Task Result(int id, RecognitionResult result) =>
        Handlers.TryGet(id, out var handler) ? handler(result) : Task.CompletedTask;
}
