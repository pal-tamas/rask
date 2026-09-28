namespace Rask.Core.Browser;

/// <summary>One live peer connection. Dispose to close it and all of its data channels.</summary>
public interface IPeerConnection : IAsyncDisposable
{
    /// <summary>
    ///     Creates an offer to send to the other peer. Pair it with
    ///     <see cref="SetLocalDescriptionAsync" /> — the browser does not apply it for you.
    /// </summary>
    ValueTask<RtcDescription> CreateOfferAsync();

    /// <summary>Creates an answer to the offer already applied with <see cref="SetRemoteDescriptionAsync" />.</summary>
    ValueTask<RtcDescription> CreateAnswerAsync();

    /// <summary>Applies our own offer/answer.</summary>
    ValueTask SetLocalDescriptionAsync(RtcDescription description);

    /// <summary>Applies the offer/answer the other peer sent.</summary>
    ValueTask SetRemoteDescriptionAsync(RtcDescription description);

    /// <summary>Adds an ICE candidate the other peer sent.</summary>
    ValueTask AddIceCandidateAsync(RtcIceCandidate candidate);

    /// <summary>
    ///     Opens a data channel. Call <see cref="IRtcDataChannel.ListenAsync" /> on the result to start
    ///     receiving. The peer sees it through <see cref="RtcHandlers.OnDataChannel" />.
    /// </summary>
    ValueTask<IRtcDataChannel> CreateDataChannelAsync(string label, RtcDataChannelOptions? options = null);

    /// <summary>
    ///     Sends a captured camera/microphone/screen stream to the peer, who receives it through
    ///     <see cref="RtcHandlers.OnTrack" />. Adding the same stream twice is a no-op. Adding or removing
    ///     a stream renegotiates, so exchange a fresh offer/answer afterwards.
    ///     <para>
    ///         The stream stays yours: disposing the connection does <b>not</b> stop it. Stop it with
    ///         <see cref="IMediaStreams.StopAsync" /> when you are done, or the camera stays open.
    ///     </para>
    /// </summary>
    ValueTask AddStreamAsync(MediaStreamId stream);

    /// <summary>
    ///     Stops sending a stream added with <see cref="AddStreamAsync" />, without stopping the stream
    ///     itself. Removing a stream that isn't being sent is a no-op.
    /// </summary>
    ValueTask RemoveStreamAsync(MediaStreamId stream);
}
