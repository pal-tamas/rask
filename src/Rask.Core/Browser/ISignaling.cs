using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>What the relay told us. Every event carries the peer it concerns, where there is one.</summary>
public sealed record SignalingHandlers
{
    /// <summary>
    ///     We joined. Carries our own peer id and the peers already in the room — the ones we should offer
    ///     to, since a peer that arrives later will offer to us instead. That asymmetry is what stops both
    ///     sides offering at once (an SDP "glare" collision neither browser resolves for you).
    /// </summary>
    public Func<string, IReadOnlyList<string>, Task>? OnJoined { get; init; }

    /// <summary>Another peer joined. Expect an offer from them; don't send one.</summary>
    public Func<string, Task>? OnPeerJoined { get; init; }

    /// <summary>A peer left. Dispose the connection you held for it.</summary>
    public Func<string, Task>? OnPeerLeft { get; init; }

    /// <summary>
    ///     A peer sent us a payload — the string they passed to <see cref="ISignalingConnection.SendAsync" />,
    ///     verbatim. Nothing between the two browsers parsed it.
    /// </summary>
    public Func<string, string, Task>? OnSignal { get; init; }

    /// <summary>The relay refused something, with its reason. The socket stays open.</summary>
    public Func<string, Task>? OnError { get; init; }

    /// <summary>The socket closed — by us, by the relay, or by the network.</summary>
    public Func<Task>? OnClosed { get; init; }
}

/// <summary>
///     Connects to the WebRTC signaling relay two peers need before they can reach each other. Works on
///     <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IWebRtc" /> deliberately doesn't pick a signaling channel — an app that already has one
///         should use it. This is the channel for apps that don't, and it pairs with the server-side relay
///         (<c>AddRaskSignaling</c> / <c>MapRaskSignaling</c> in <c>Rask.Server</c>). The payload is an opaque
///         string end to end: serialize an <see cref="RtcDescription" /> or an <see cref="RtcIceCandidate" />
///         into it however you like — nothing between the two browsers looks inside.
///     </para>
///     <para>
///         The socket is separate from the live render socket, and lives in the browser on both hosts, so a
///         Server-hosted app doesn't put its own server in the middle of a relay it is already running.
///     </para>
/// </remarks>
public interface ISignaling
{
    /// <summary>Whether the browser can open a WebSocket at all.</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Connects to the relay and joins <paramref name="room" />. Dispose the result to leave — the other
    ///     peers are told.
    /// </summary>
    /// <param name="room">The room id. Opaque to the framework; the server decides who may join one.</param>
    /// <param name="handlers">The callbacks the relay pushes into.</param>
    /// <param name="path">The relay's path. Must match the server's <c>RaskSignalingOptions.Path</c>.</param>
    ValueTask<ISignalingConnection> JoinAsync(
        string room, SignalingHandlers handlers, string path = "/rask/signaling");
}

/// <summary>An open signaling connection. Dispose to leave the room and close the socket.</summary>
public interface ISignalingConnection : IAsyncDisposable
{
    /// <summary>
    ///     Sends <paramref name="payload" /> to one peer in our room. The relay refuses a peer that isn't in
    ///     it, and never delivers a message back to its sender.
    /// </summary>
    ValueTask SendAsync(string toPeerId, string payload);
}

/// <summary>
///     Infrastructure for <see cref="ISignaling" /> — routes relay messages back to the right C# callbacks
///     by connection id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskSignal</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SignalingInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, SignalingHandlers> Handlers = new();

    internal static int Register(SignalingHandlers handlers)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handlers;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge for each relay message; do not call.</summary>
    [JSInvokable("RaskSignalMessage")]
    public static Task Message(int id, string type, string peerId, string payload)
    {
        if (!Handlers.TryGetValue(id, out var h))
        {
            return Task.CompletedTask;
        }

        return type switch
        {
            // `payload` carries the peer list for a join — the one message where it isn't an app payload.
            "joined" => h.OnJoined is null ? Task.CompletedTask : h.OnJoined(peerId, ParsePeers(payload)),
            "peer-joined" => h.OnPeerJoined is null ? Task.CompletedTask : h.OnPeerJoined(peerId),
            "peer-left" => h.OnPeerLeft is null ? Task.CompletedTask : h.OnPeerLeft(peerId),
            "signal" => h.OnSignal is null ? Task.CompletedTask : h.OnSignal(peerId, payload),
            "error" => h.OnError is null ? Task.CompletedTask : h.OnError(payload),
            _ => Task.CompletedTask
        };
    }

    /// <summary>Infrastructure. Invoked by the JS bridge when the socket closes; do not call.</summary>
    [JSInvokable("RaskSignalClosed")]
    public static Task Closed(int id)
    {
        if (!Handlers.TryRemove(id, out var h))
        {
            return Task.CompletedTask;
        }

        return h.OnClosed is null ? Task.CompletedTask : h.OnClosed();
    }

    private static IReadOnlyList<string> ParsePeers(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var peers = new List<string>(document.RootElement.GetArrayLength());
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    peers.Add(element.GetString()!);
                }
            }

            return peers;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
///     Default <see cref="ISignaling" />, backed by the unified <see cref="IJSRuntime" /> and the framework's
///     <c>__raskSignal</c> helper, which owns the WebSocket.
/// </summary>
public sealed class Signaling : ISignaling
{
    private readonly IJSRuntime _js;

    // Root SignalingInterop's [JSInvokable]s for the WASM trimmer — they're reached only via the JS
    // DotNetDispatcher (reflection), so without this they could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(SignalingInterop))]
    public Signaling(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskSignal.isSupported");

    /// <inheritdoc />
    public async ValueTask<ISignalingConnection> JoinAsync(
        string room, SignalingHandlers handlers, string path = "/rask/signaling")
    {
        ArgumentException.ThrowIfNullOrEmpty(room);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Register before connecting: the relay answers a join immediately, and a handler registered after
        // the fact would miss the peer list it replies with.
        var id = SignalingInterop.Register(handlers);
        try
        {
            await _js.InvokeVoidAsync("__raskSignal.open", id, path);
            await _js.InvokeVoidAsync("__raskSignal.send", id, Join(room));
        }
        catch
        {
            SignalingInterop.Unregister(id);
            throw;
        }

        return new Connection(_js, id);
    }

    private static string Join(string room) => JsonSerializer.Serialize(
        new SignalingJoin("join", room), RaskBrowserJsonContext.Default.SignalingJoin);

    private sealed class Connection(IJSRuntime js, int id) : ISignalingConnection
    {
        private bool _disposed;

        public ValueTask SendAsync(string toPeerId, string payload)
        {
            ArgumentException.ThrowIfNullOrEmpty(toPeerId);
            ArgumentNullException.ThrowIfNull(payload);

            var json = JsonSerializer.Serialize(
                new SignalingSignal("signal", toPeerId, payload),
                RaskBrowserJsonContext.Default.SignalingSignal);
            return js.InvokeVoidAsync("__raskSignal.send", id, json);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SignalingInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskSignal.close", id);
        }
    }
}

/// <summary>The join frame, as the relay expects it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SignalingJoin(string Type, string Room);

/// <summary>The relay frame carrying one peer-to-peer payload.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SignalingSignal(string Type, string To, string Payload);
