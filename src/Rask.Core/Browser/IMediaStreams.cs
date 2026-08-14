using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Identifies one live <c>MediaStream</c> held in the browser. A <c>MediaStream</c> can't cross interop,
///     so the framework keeps it under this id and C# passes the id around instead — to attach it to a
///     <c>&lt;video&gt;</c>, to stop it, or to send it to a WebRTC peer.
/// </summary>
/// <remarks>
///     You get one from <c>IMediaDevices</c> (WASM), from <see cref="Rask.Core.Components.MediaCaptureTrigger" />
///     (every host), or from a peer's remote stream via <c>RtcHandlers.OnTrack</c>. It lives in
///     <c>Rask.Core</c> rather than beside <c>IMediaDevices</c> so every host — and
///     <see cref="IWebRtc" /> — can name one without depending on the WASM-only capture service.
/// </remarks>
/// <param name="Value">The browser-side id. Opaque; only meaningful to the framework's JS helpers.</param>
public readonly record struct MediaStreamId(int Value);

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
