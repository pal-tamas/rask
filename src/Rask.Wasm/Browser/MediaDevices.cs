using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="IMediaDevices" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>MediaStream</c> is opaque to C#, so the framework's <c>__raskMedia</c> helper holds each under a
///     minted id; the handle attaches the stream to a video element (handed across as an
///     <see cref="ElementRef" />) and stops its tracks by id.
/// </summary>
public sealed class MediaDevices(IJSRuntime js) : IMediaDevices
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskMedia.isSupported");

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<MediaDeviceInfo>> EnumerateDevicesAsync() =>
        await js.InvokeAsync<MediaDeviceInfo[]>("__raskMedia.enumerate").ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<IMediaStreamHandle> GetUserMediaAsync(MediaConstraints constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        var id = await js.InvokeAsync<int>("__raskMedia.getUserMedia", constraints).ConfigureAwait(false);
        return new StreamHandle(js, id);
    }

    /// <inheritdoc />
    public async ValueTask<IMediaStreamHandle> GetDisplayMediaAsync()
    {
        var id = await js.InvokeAsync<int>("__raskMedia.getDisplayMedia").ConfigureAwait(false);
        return new StreamHandle(js, id);
    }

    private sealed class StreamHandle(IJSRuntime js, int id) : IMediaStreamHandle
    {
        private bool _stopped;

        public MediaStreamId Id => new(id);

        public ValueTask AttachToAsync(ElementRef video)
        {
            ArgumentNullException.ThrowIfNull(video);
            return js.InvokeVoidAsync("__raskMedia.attach", id, video);
        }

        public ValueTask StopAsync()
        {
            if (_stopped)
            {
                return ValueTask.CompletedTask;
            }

            _stopped = true;
            return js.InvokeVoidAsync("__raskMedia.stop", id);
        }

        public ValueTask DisposeAsync() => StopAsync();
    }
}
