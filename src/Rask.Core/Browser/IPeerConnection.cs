using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>One live peer connection. Dispose to close it and all of its data channels.</summary>
public interface IPeerConnection : IAsyncDisposable
{
    /// <summary>
    ///     Creates an offer to send to the other peer. Pair it with
    ///     <see cref="SetLocalDescription" /> — the browser does not apply it for you.
    /// </summary>
    ValueTask<RtcDescription> CreateOffer();

    /// <summary>Creates an answer to the offer already applied with <see cref="SetRemoteDescription" />.</summary>
    ValueTask<RtcDescription> CreateAnswer();

    /// <summary>Applies our own offer/answer.</summary>
    ValueTask SetLocalDescription(RtcDescription description);

    /// <summary>Applies the offer/answer the other peer sent.</summary>
    ValueTask SetRemoteDescription(RtcDescription description);

    /// <summary>Adds an ICE candidate the other peer sent.</summary>
    ValueTask AddIceCandidate(RtcIceCandidate candidate);

    /// <summary>
    ///     Opens a data channel. Call <see cref="IRtcDataChannel.Listen" /> on the result to start
    ///     receiving. The peer sees it through <see cref="RtcHandlers.OnDataChannel" />.
    /// </summary>
    ValueTask<IRtcDataChannel> CreateDataChannel(string label, RtcDataChannelOptions? options = null);

    /// <summary>
    ///     Sends a captured camera/microphone/screen stream to the peer, who receives it through
    ///     <see cref="RtcHandlers.OnTrack" />: the handle <c>Trigger.MediaCapture</c>'s <c>OnStream</c> hands you.
    ///     Adding the same stream twice is a no-op. Adding or removing a stream renegotiates, so exchange a fresh
    ///     offer/answer afterwards.
    ///     <para>
    ///         The stream stays yours: disposing the connection does <b>not</b> stop it. Stop its tracks when you
    ///         are done (Rask.Web: <c>MediaStream.From(stream).GetTracks()</c>, then <c>Stop()</c> each), or the
    ///         camera stays open.
    ///     </para>
    /// </summary>
    ValueTask AddStream(IJSObjectReference stream);

    /// <summary>
    ///     Stops sending a stream added with <see cref="AddStream" />, without stopping the stream
    ///     itself. Removing a stream that isn't being sent is a no-op.
    /// </summary>
    ValueTask RemoveStream(IJSObjectReference stream);
}
