using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Browser;

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
        TryOwned(id, out var r) && r.Handlers.OnIceCandidates is not null
            ? r.Handlers.OnIceCandidates(candidates)
            : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by the JS bridge when the connection state changes; do not call.</summary>
    [JSInvokable("RaskRtcState")]
    public static Task State(int id, string state) =>
        TryOwned(id, out var r) && r.Handlers.OnConnectionStateChanged is not null
            ? r.Handlers.OnConnectionStateChanged(Parse(state))
            : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by the JS bridge when the remote peer opens a channel; do not call.</summary>
    [JSInvokable("RaskRtcChannel")]
    public static Task Channel(int connectionId, int channelId, string label)
    {
        if (!TryOwned(connectionId, out var r) || r.Handlers.OnDataChannel is null)
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
        if (!TryOwned(connectionId, out _) || !Channels.TryGetValue((connectionId, channelId), out var handler))
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
        TryOwned(id, out var r) && r.Handlers.OnTrack is not null
            ? r.Handlers.OnTrack(new MediaStreamId(streamId))
            : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by the JS bridge when a channel closes; do not call.</summary>
    [JSInvokable("RaskRtcChannelClosed")]
    public static Task ChannelClosed(int connectionId, int channelId)
    {
        if (!TryOwned(connectionId, out _))
        {
            return Task.CompletedTask;
        }

        UnregisterChannel(connectionId, channelId);
        return Task.CompletedTask;
    }

    // Only the session that opened the connection may push into it (see JsCallbacks).
    private static bool TryOwned(int id, [NotNullWhen(true)] out Registration? registration)
    {
        registration = Connections.TryGetValue(id, out var r) && JsCaller.Owns(r.Js) ? r : null;
        return registration is not null;
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
