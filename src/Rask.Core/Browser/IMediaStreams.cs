namespace Rask.Core.Browser;

/// <summary>
///     Attach or stop a live media stream, wherever it came from
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/MediaStream" />). Works on <b>both
///     transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         Neither call needs a user gesture — only <em>acquiring</em> a stream does — which is why this is
///         a transport-agnostic service while <c>IMediaDevices</c> is WASM-only. On the Server host, pair it
///         with <see cref="Rask.Core.Components.MediaCaptureTrigger" />: the trigger acquires the camera
///         inside the click and hands you a <see cref="MediaStreamId" /> through its <c>OnStream</c>
///         callback, and from there the stream is yours to re-attach, stop, or send to a peer.
///     </para>
///     <para>
///         <b>Stopping is not optional.</b> A stream holds the camera and microphone open, hardware
///         indicator and all, until every track is stopped. Stop it when the component unmounts.
///     </para>
/// </remarks>
public interface IMediaStreams
{
    /// <summary>
    ///     Attaches <paramref name="stream" /> to a <c>&lt;video&gt;</c> element and plays it (muted, so
    ///     autoplay is allowed). A stream that has been stopped, or an element that isn't in the document,
    ///     is a no-op rather than an error.
    /// </summary>
    ValueTask AttachAsync(MediaStreamId stream, ElementRef video);

    /// <summary>
    ///     Stops every track on <paramref name="stream" />, releasing the camera/microphone. Stopping an
    ///     already-stopped stream is a no-op.
    /// </summary>
    ValueTask StopAsync(MediaStreamId stream);
}
