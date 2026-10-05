using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Callbacks the browser pushes into for one peer connection. Every one is optional; leave a callback
///     null and the framework never asks the browser for it.
/// </summary>
public sealed record RtcHandlers
{
    /// <summary>
    ///     Local ICE candidates to forward to the other peer over your signaling channel. Delivered in
    ///     batches — gathering emits a burst, and one push per candidate would be a WebSocket frame each on
    ///     the Server host.
    /// </summary>
    public Func<IReadOnlyList<RtcIceCandidate>, Task>? OnIceCandidates { get; init; }

    /// <summary>The connection's state changed.</summary>
    public Func<RtcConnectionState, Task>? OnConnectionStateChanged { get; init; }

    /// <summary>
    ///     The <b>remote</b> peer opened a data channel. Call <see cref="IRtcDataChannel.ListenAsync" /> on
    ///     it to start receiving; anything the peer already sent is buffered and arrives in the first batch.
    /// </summary>
    public Func<IRtcDataChannel, Task>? OnDataChannel { get; init; }

    /// <summary>
    ///     The <b>remote</b> peer's media arrived, as a handle to its <c>MediaStream</c>: show it with Rask.Web's
    ///     <c>await _video.SetSrcObject(MediaStream.From(stream))</c>. Fires once per stream, not per track — a
    ///     peer sending camera and microphone sends two tracks in one stream, and the stream is what you show.
    ///     The stream is stopped for you when the connection is disposed; dispose of the handle when you are done.
    /// </summary>
    public Func<IJSObjectReference, Task>? OnTrack { get; init; }
}
