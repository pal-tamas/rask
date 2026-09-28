using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IMediaStreams" />, backed by the unified <see cref="IJSRuntime" /> and the
///     framework's <c>__raskMedia</c> helper — the same id-keyed map that <c>IMediaDevices</c> and the
///     <c>media.start</c> gesture capability write into.
/// </summary>
public sealed class MediaStreams(IJSRuntime js) : IMediaStreams
{
    /// <inheritdoc />
    public ValueTask AttachAsync(MediaStreamId stream, ElementRef video)
    {
        ArgumentNullException.ThrowIfNull(video);
        return js.InvokeVoidAsync("__raskMedia.attach", stream.Value, video);
    }

    /// <inheritdoc />
    public ValueTask StopAsync(MediaStreamId stream) => js.InvokeVoidAsync("__raskMedia.stop", stream.Value);
}
