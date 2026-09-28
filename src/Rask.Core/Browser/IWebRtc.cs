namespace Rask.Core.Browser;

/// <summary>
///     Typed access to WebRTC
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API" />) — connect two browsers
///     directly for peer-to-peer data. Works on <b>both transports</b>; inject it through a component
///     constructor.
/// </summary>
/// <remarks>
///     <para>
///         <b>You supply the signaling.</b> WebRTC cannot start a connection on its own: the two peers have
///         to trade an offer, an answer, and their ICE candidates through some channel you already have —
///         a WebSocket, an HTTP endpoint, even <see cref="IBroadcastChannel" /> between two tabs of the
///         same origin. <see cref="RtcDescription" /> and <see cref="RtcIceCandidate" /> are plain
///         serializable records so they can ride whatever you use.
///     </para>
///     <para>
///         <b>Events are batched.</b> Incoming messages and ICE candidates arrive as lists, not one call
///         each. On the Server host every push costs an inbound WebSocket frame, and the host closes a
///         socket that exceeds its inbound frame rate — a busy channel delivered one-message-per-push would
///         end the session. The list is the same shape on WASM, so the two hosts stay identical.
///     </para>
///     <para>
///         The browser pushes into your callbacks (via a static <c>[JSInvokable]</c>, so one wiring serves
///         both transports). A callback that updates state should call <c>StateHasChanged()</c> — it's a
///         subscription, not a render/binding callback, so RASK026 doesn't apply. Dispose the connection on
///         unmount; that closes its channels too.
///     </para>
///     <code>
///     _conn = await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers
///     {
///         OnIceCandidates = async cands => { foreach (var c in cands) await signaling.SendAsync(c); },
///         OnDataChannel = async ch => await ch.ListenAsync(OnMessagesAsync),
///     });
///     var chat = await _conn.CreateDataChannelAsync("chat");
///     await chat.ListenAsync(OnMessagesAsync);
///     await signaling.SendAsync(await _conn.CreateOfferAsync());
///     </code>
/// </remarks>
public interface IWebRtc
{
    /// <summary>Whether the browser supports WebRTC (<c>window.RTCPeerConnection</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Creates a peer connection. Dispose it to close the connection and every channel on it.
    /// </summary>
    /// <param name="config">ICE servers and transport policy.</param>
    /// <param name="handlers">The callbacks the browser pushes into.</param>
    ValueTask<IPeerConnection> CreateAsync(RtcConfiguration config, RtcHandlers handlers);
}
