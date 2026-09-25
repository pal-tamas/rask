using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
