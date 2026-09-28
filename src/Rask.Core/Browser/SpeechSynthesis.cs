using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
