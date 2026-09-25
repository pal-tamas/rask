using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
