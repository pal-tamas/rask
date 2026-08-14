using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;
using Rask.Core.Diagnostics;

namespace Rask.Core.Browser;

/// <summary>How a peer connection reaches the other side.</summary>
public sealed record RtcConfiguration
{
    /// <summary>
    ///     STUN/TURN server URLs (<c>stun:</c>, <c>turn:</c> or <c>turns:</c>). Empty — the default — means
    ///     host candidates only, which connects peers on the same machine or LAN but not across the
    ///     internet. Rask ships no STUN or TURN server; supply your own.
    /// </summary>
    public string[]? IceServers { get; init; }

    /// <summary>
    ///     <c>"all"</c> (default) or <c>"relay"</c>. <c>"relay"</c> forces traffic through a TURN server, so
    ///     the peer never learns your local network addresses — the setting to use when that leak matters.
    /// </summary>
    public string? IceTransportPolicy { get; init; }
}

/// <summary>One end of the offer/answer exchange — an SDP blob plus its role.</summary>
/// <param name="Type"><c>"offer"</c> or <c>"answer"</c>.</param>
/// <param name="Sdp">The session description.</param>
public sealed record RtcDescription(string Type, string Sdp);

/// <summary>One ICE candidate, to be handed to the other peer by your signaling channel.</summary>
/// <param name="Candidate">The candidate line.</param>
/// <param name="SdpMid">The media stream identification this candidate belongs to.</param>
/// <param name="SdpMLineIndex">The index of the media description this candidate belongs to.</param>
public sealed record RtcIceCandidate(string Candidate, string? SdpMid, int? SdpMLineIndex);

/// <summary>Tuning for a data channel (the <c>RTCDataChannelInit</c> options).</summary>
public sealed record RtcDataChannelOptions
{
    /// <summary>Whether messages arrive in the order they were sent. Defaults to <c>true</c>.</summary>
    public bool? Ordered { get; init; }

    /// <summary>How many times to retry a lost message before giving up. <c>null</c> retries indefinitely.</summary>
    public int? MaxRetransmits { get; init; }

    /// <summary>An application-defined sub-protocol name, echoed to the peer.</summary>
    public string? Protocol { get; init; }
}

/// <summary>
///     One message received on a data channel. Exactly one of <see cref="Text" /> / <see cref="Data" /> is
///     set, matching what the peer sent.
/// </summary>
/// <param name="Text">The message, when the peer sent a string.</param>
/// <param name="Data">The message, when the peer sent binary.</param>
public sealed record RtcMessage(string? Text, byte[]? Data);

/// <summary>The lifecycle of a peer connection (<c>RTCPeerConnection.connectionState</c>).</summary>
public enum RtcConnectionState
{
    /// <summary>Created, but no ICE work has started.</summary>
    New,

    /// <summary>Connectivity checks are in progress.</summary>
    Connecting,

    /// <summary>Usable — media and data can flow.</summary>
    Connected,

    /// <summary>Connectivity lost; may recover on its own.</summary>
    Disconnected,

    /// <summary>Connectivity lost for good.</summary>
    Failed,

    /// <summary>Closed, by either side.</summary>
    Closed
}

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
    ///     The <b>remote</b> peer's media arrived. Attach it to a <c>&lt;video&gt;</c> with
    ///     <see cref="IMediaStreams.AttachAsync" />. Fires once per stream, not per track — a peer sending
    ///     camera and microphone sends two tracks in one stream, and the stream is what you attach. The
    ///     stream is stopped for you when the connection is disposed.
    /// </summary>
    public Func<MediaStreamId, Task>? OnTrack { get; init; }
}

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

/// <summary>One data channel on a peer connection. Dispose to close it.</summary>
public interface IRtcDataChannel : IAsyncDisposable
{
    /// <summary>The channel's label, as both peers see it.</summary>
    string Label { get; }

    /// <summary>
    ///     Starts delivering messages to <paramref name="onMessages" />. Messages the peer sent before this
    ///     call are buffered by the framework and ride the first batch, so a channel opened by the remote
    ///     peer loses nothing between arriving and being listened to. Calling it again replaces the handler.
    /// </summary>
    ValueTask ListenAsync(Func<IReadOnlyList<RtcMessage>, Task> onMessages);

    /// <summary>Sends a string to the other peer.</summary>
    ValueTask SendAsync(string text);

    /// <summary>Sends bytes to the other peer.</summary>
    ValueTask SendAsync(byte[] data);
}

/// <summary>
///     One message as it crosses the interop boundary — binary rides base64-encoded, because
///     <c>byte[]</c> doesn't marshal.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record RtcMessageWire(string? Text, string? Data);

/// <summary>
///     Infrastructure for <see cref="IWebRtc" /> — routes pushed ICE candidates, state changes, channels
///     and messages back to the right C# callbacks by id. <b>Not for application use;</b> invoked only by
///     the framework's <c>__raskRtc</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class WebRtcInterop
{
    private static int _nextConnection;

    // Connection ids are minted here, so they are unique across the whole process — which matters on the
    // Server host, where one process serves many sessions. Channel ids are minted by JS (a remote peer can
    // open a channel at any moment, so one minting side keeps that id space single), and a client's
    // channel #1 is therefore NOT unique across sessions. Every channel registry is keyed by the pair, and
    // the connection half of it makes the key process-unique again.
    private static readonly ConcurrentDictionary<int, Registration> Connections = new();

    private static readonly ConcurrentDictionary<(int Connection, int Channel),
        Func<IReadOnlyList<RtcMessage>, Task>> Channels = new();

    private static readonly ConcurrentDictionary<(int Connection, int Channel), IRtcDataChannel>
        ChannelHandles = new();

    // The runtime is held per connection rather than in a static seam, for the same reason: on Server each
    // session has its own scoped IJSRuntime, and a remote-opened channel has to be adopted with the runtime
    // of the session that owns the connection — not whichever one happened to register last.
    private sealed record Registration(RtcHandlers Handlers, IJSRuntime Js);

    internal static int RegisterConnection(RtcHandlers handlers, IJSRuntime js)
    {
        var id = Interlocked.Increment(ref _nextConnection);
        Connections[id] = new Registration(handlers, js);
        return id;
    }

    internal static void UnregisterConnection(int id)
    {
        Connections.TryRemove(id, out _);
        foreach (var key in Channels.Keys)
        {
            if (key.Connection == id)
            {
                UnregisterChannel(id, key.Channel);
            }
        }

        foreach (var key in ChannelHandles.Keys)
        {
            if (key.Connection == id)
            {
                UnregisterChannel(id, key.Channel);
            }
        }
    }

    internal static void RegisterChannel(int connectionId, int channelId, IRtcDataChannel handle) =>
        ChannelHandles[(connectionId, channelId)] = handle;

    internal static void Listen(
        int connectionId, int channelId, Func<IReadOnlyList<RtcMessage>, Task> onMessages) =>
        Channels[(connectionId, channelId)] = onMessages;

    internal static void UnregisterChannel(int connectionId, int channelId)
    {
        Channels.TryRemove((connectionId, channelId), out _);
        ChannelHandles.TryRemove((connectionId, channelId), out _);
    }

    /// <summary>Infrastructure. Invoked by the JS bridge with a batch of local ICE candidates; do not call.</summary>
    [JSInvokable("RaskRtcIce")]
    public static Task Ice(int id, RtcIceCandidate[] candidates) =>
        Connections.TryGetValue(id, out var r) && r.Handlers.OnIceCandidates is not null
            ? r.Handlers.OnIceCandidates(candidates)
            : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by the JS bridge when the connection state changes; do not call.</summary>
    [JSInvokable("RaskRtcState")]
    public static Task State(int id, string state) =>
        Connections.TryGetValue(id, out var r) && r.Handlers.OnConnectionStateChanged is not null
            ? r.Handlers.OnConnectionStateChanged(Parse(state))
            : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by the JS bridge when the remote peer opens a channel; do not call.</summary>
    [JSInvokable("RaskRtcChannel")]
    public static Task Channel(int connectionId, int channelId, string label)
    {
        if (!Connections.TryGetValue(connectionId, out var r) || r.Handlers.OnDataChannel is null)
        {
            return Task.CompletedTask;
        }

        if (!ChannelHandles.TryGetValue((connectionId, channelId), out var handle))
        {
            handle = new WebRtc.DataChannel(r.Js, connectionId, channelId, label);
            ChannelHandles[(connectionId, channelId)] = handle;
        }

        return r.Handlers.OnDataChannel(handle);
    }

    /// <summary>Infrastructure. Invoked by the JS bridge with a batch of received messages; do not call.</summary>
    [JSInvokable("RaskRtcMessages")]
    public static Task Messages(int connectionId, int channelId, RtcMessageWire[] messages, int dropped)
    {
        if (!Channels.TryGetValue((connectionId, channelId), out var handler))
        {
            return Task.CompletedTask;
        }

        if (dropped > 0)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.WebRtc",
                $"WebRTC data channel '{channelId}' dropped {dropped} message(s): the peer sent faster "
                + "than the app consumed and the client-side buffer was full.");
        }

        var decoded = new RtcMessage[messages.Length];
        for (var i = 0; i < messages.Length; i++)
        {
            var m = messages[i];
            decoded[i] = new RtcMessage(m.Text, m.Data is null ? null : Convert.FromBase64String(m.Data));
        }

        return handler(decoded);
    }

    /// <summary>Infrastructure. Invoked by the JS bridge when the peer's media arrives; do not call.</summary>
    [JSInvokable("RaskRtcTrack")]
    public static Task Track(int id, int streamId) =>
        Connections.TryGetValue(id, out var r) && r.Handlers.OnTrack is not null
            ? r.Handlers.OnTrack(new MediaStreamId(streamId))
            : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by the JS bridge when a channel closes; do not call.</summary>
    [JSInvokable("RaskRtcChannelClosed")]
    public static Task ChannelClosed(int connectionId, int channelId)
    {
        UnregisterChannel(connectionId, channelId);
        return Task.CompletedTask;
    }

    private static RtcConnectionState Parse(string state) => state switch
    {
        "connecting" => RtcConnectionState.Connecting,
        "connected" => RtcConnectionState.Connected,
        "disconnected" => RtcConnectionState.Disconnected,
        "failed" => RtcConnectionState.Failed,
        "closed" => RtcConnectionState.Closed,
        _ => RtcConnectionState.New
    };
}

/// <summary>
///     Default <see cref="IWebRtc" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>RTCPeerConnection</c> and its channels stay in the browser under the framework's
///     <c>__raskRtc</c> helper, addressed by id; every push comes back through
///     <see cref="WebRtcInterop" />.
/// </summary>
public sealed class WebRtc : IWebRtc
{
    private readonly IJSRuntime _js;

    // Root WebRtcInterop's [JSInvokable]s for the WASM trimmer — they're reached only via the JS
    // DotNetDispatcher (reflection), so without this they could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(WebRtcInterop))]
    public WebRtc(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskRtc.isSupported");

    /// <inheritdoc />
    public async ValueTask<IPeerConnection> CreateAsync(RtcConfiguration config, RtcHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(handlers);
        Validate(config);

        // Register before creating: ICE gathering starts as soon as a description is set, and a handler
        // registered afterwards would miss the first candidates.
        var id = WebRtcInterop.RegisterConnection(handlers, _js);
        try
        {
            await _js.InvokeVoidAsync("__raskRtc.create", id, config);
        }
        catch
        {
            WebRtcInterop.UnregisterConnection(id);
            throw;
        }

        return new PeerConnection(_js, id);
    }

    // An ICE server URL comes from app configuration and is handed to the browser verbatim. Anything that
    // isn't a STUN/TURN scheme would be a misconfiguration at best, so reject it here rather than let the
    // browser decide what to do with it.
    private static void Validate(RtcConfiguration config)
    {
        foreach (var url in config.IceServers ?? [])
        {
            if (!url.StartsWith("stun:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("turn:", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("turns:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"ICE server URL '{url}' must use the stun:, turn: or turns: scheme.", nameof(config));
            }
        }

        if (config.IceTransportPolicy is not (null or "all" or "relay"))
        {
            throw new ArgumentException(
                $"IceTransportPolicy must be \"all\" or \"relay\", not '{config.IceTransportPolicy}'.",
                nameof(config));
        }
    }

    private sealed class PeerConnection(IJSRuntime js, int id) : IPeerConnection
    {
        private bool _disposed;

        public ValueTask<RtcDescription> CreateOfferAsync() =>
            js.InvokeAsync<RtcDescription>("__raskRtc.createOffer", id);

        public ValueTask<RtcDescription> CreateAnswerAsync() =>
            js.InvokeAsync<RtcDescription>("__raskRtc.createAnswer", id);

        public ValueTask SetLocalDescriptionAsync(RtcDescription description)
        {
            ArgumentNullException.ThrowIfNull(description);
            return js.InvokeVoidAsync("__raskRtc.setLocal", id, description);
        }

        public ValueTask SetRemoteDescriptionAsync(RtcDescription description)
        {
            ArgumentNullException.ThrowIfNull(description);
            return js.InvokeVoidAsync("__raskRtc.setRemote", id, description);
        }

        public ValueTask AddIceCandidateAsync(RtcIceCandidate candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            return js.InvokeVoidAsync("__raskRtc.addIce", id, candidate);
        }

        public ValueTask AddStreamAsync(MediaStreamId stream) =>
            js.InvokeVoidAsync("__raskRtc.addStream", id, stream.Value);

        public ValueTask RemoveStreamAsync(MediaStreamId stream) =>
            js.InvokeVoidAsync("__raskRtc.removeStream", id, stream.Value);

        public async ValueTask<IRtcDataChannel> CreateDataChannelAsync(
            string label, RtcDataChannelOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(label);

            var channelId = await js.InvokeAsync<int>("__raskRtc.createChannel", id, label, options);
            var channel = new DataChannel(js, id, channelId, label);
            WebRtcInterop.RegisterChannel(id, channelId, channel);
            return channel;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            WebRtcInterop.UnregisterConnection(id);
            await js.InvokeVoidAsync("__raskRtc.close", id);
        }
    }

    // Internal rather than private: WebRtcInterop builds one of these when the remote peer opens a
    // channel, using the runtime of the session that owns the connection.
    internal sealed class DataChannel(IJSRuntime js, int connectionId, int id, string label) : IRtcDataChannel
    {
        private bool _disposed;

        public string Label => label;

        public ValueTask ListenAsync(Func<IReadOnlyList<RtcMessage>, Task> onMessages)
        {
            ArgumentNullException.ThrowIfNull(onMessages);
            WebRtcInterop.Listen(connectionId, id, onMessages);
            return js.InvokeVoidAsync("__raskRtc.listen", id);
        }

        public ValueTask SendAsync(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return js.InvokeVoidAsync("__raskRtc.sendText", id, text);
        }

        public ValueTask SendAsync(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return js.InvokeVoidAsync("__raskRtc.sendBytes", id, Convert.ToBase64String(data));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            WebRtcInterop.UnregisterChannel(connectionId, id);
            await js.InvokeVoidAsync("__raskRtc.closeChannel", id);
        }
    }
}
