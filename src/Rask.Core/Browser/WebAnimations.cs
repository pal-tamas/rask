using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IWebAnimations" />, backed by the unified <see cref="IJSRuntime" /> and the
///     shared <c>__raskAnim</c> helper both client runtimes splice in.
/// </summary>
public sealed class WebAnimations(IJSRuntime js) : IWebAnimations
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskAnim.supported");

    /// <inheritdoc />
    public async ValueTask<AnimationId> StartAsync(
        ElementRef element,
        IReadOnlyDictionary<string, string[]> keyframes,
        AnimationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(keyframes);

        // A concrete Dictionary, not the interface: the source-generated JSON context registers
        // Dictionary<string, string[]>, and serializing through the interface would fall off that
        // contract and break the trimmed WASM publish.
        var frames = keyframes as Dictionary<string, string[]> ?? new Dictionary<string, string[]>(keyframes, StringComparer.Ordinal);
        var id = await js.InvokeAsync<int>("__raskAnim.start", element, frames, options ?? new AnimationOptions())
            .ConfigureAwait(false);
        return new AnimationId(id);
    }

    /// <inheritdoc />
    public ValueTask CancelAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.cancel", animation.Value);

    /// <inheritdoc />
    public ValueTask FinishAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.finish", animation.Value);

    /// <inheritdoc />
    public ValueTask PauseAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.pause", animation.Value);

    /// <inheritdoc />
    public ValueTask PlayAsync(AnimationId animation) => js.InvokeVoidAsync("__raskAnim.play", animation.Value);

    /// <inheritdoc />
    public ValueTask<bool> WaitAsync(AnimationId animation) =>
        js.InvokeAsync<bool>("__raskAnim.finished", animation.Value);
}
