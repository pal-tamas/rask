using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Wasm.Browser;

/// <summary>A handle to one live <c>MediaStream</c>. Dispose (or <see cref="StopAsync" />) to stop all tracks.</summary>
public interface IMediaStreamHandle : IAsyncDisposable
{
    /// <summary>
    ///     The stream's framework id — the same currency <see cref="IMediaStreams" />,
    ///     <c>MediaCaptureTrigger</c> and <see cref="IWebRtc" /> deal in. Pass it to
    ///     <c>IPeerConnection.AddStreamAsync</c> to send this stream to a peer.
    /// </summary>
    MediaStreamId Id { get; }

    /// <summary>Shows the stream in the <c>&lt;video&gt;</c> referenced by <paramref name="video" /> and plays it.</summary>
    ValueTask AttachToAsync(ElementRef video);

    /// <summary>Stops every track, releasing the camera/microphone (turns off the hardware indicator).</summary>
    ValueTask StopAsync();
}
