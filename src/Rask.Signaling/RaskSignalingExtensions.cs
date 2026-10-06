using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Hosting.Shared;

namespace Rask.Signaling;

/// <summary>
///     Hosts the WebRTC signaling relay two peers need before they can connect
///     (see <c>docs/apis/webrtc.md</c>). Opt-in: call <see cref="AddRaskSignaling" /> and
///     <see cref="MapRaskSignaling" />.
/// </summary>
/// <remarks>
///     <para>
///         The relay carries <b>opaque strings</b>. It never parses an SDP or an ICE candidate — the payload
///         is whatever the client put there, forwarded verbatim to one named peer in the same room. That is
///         deliberate: parsing attacker-controlled SDP server-side would add a lot of surface for no benefit,
///         since only the browsers need to understand it.
///     </para>
///     <para>
///         <b>It is a relay between untrusted peers</b>, so: peer ids are minted by the server and never
///         taken from the client; a message is only delivered to a peer in the sender's own room; nothing is
///         ever echoed back to its sender; payloads and message rates are capped; and joining requires
///         authentication by default, with <see cref="SignalingOptions.AuthorizeRoom" /> as the hook for
///         "may this user join <em>this</em> room".
///     </para>
/// </remarks>
public static class RaskSignalingExtensions
{
    /// <summary>Registers the signaling relay. Pair with <see cref="MapRaskSignaling" />.</summary>
    /// <remarks>
    ///     <see cref="SignalingOptions" /> reads the <c>Rask:Signaling</c> configuration section first, then
    ///     <paramref name="configure" />, so code wins. <see cref="SignalingOptions.AuthorizeRoom" /> is a
    ///     delegate and can only be set in code. Idempotent: the first call's options win.
    /// </remarks>
    public static IServiceCollection AddRaskSignaling(
        this IServiceCollection services, Action<SignalingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Validated when the host starts — and MapRaskSignaling resolves the options while mapping, so a bad
        // value stops the app before a single peer can connect either way.
        services.AddRaskOptions("Rask:Signaling", static (section, o) => section.Bind(o), configure, Validate);
        services.TryAddSingleton<SignalingHub>();
        return services;
    }

    /// <summary>
    ///     Maps the signaling WebSocket endpoint at <see cref="SignalingOptions.Path" />. Requires
    ///     <see cref="AddRaskSignaling" />.
    /// </summary>
    public static IEndpointRouteBuilder MapRaskSignaling(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<SignalingOptions>()
                      ?? throw new InvalidOperationException(
                          "MapRaskSignaling() needs AddRaskSignaling() — the relay's options and room "
                          + "registry are resolved from DI.");

        var builder = endpoints.MapGet(options.Path, RunAsync);

        // Authorization is the default, and it is expressed on the endpoint so a host's own policies apply.
        // The opt-out is explicit rather than implied by the absence of a call.
        if (options.RequireAuthorization)
        {
            builder.RequireAuthorization();
        }
        else
        {
            builder.AllowAnonymous();
        }

        return endpoints;
    }

    // Every failure names the setting ("MaxPeersPerRoom (1) must be at least 2.") — the key someone has to go
    // and fix; the options registration prefixes the section.
    private static void Validate(SignalingOptions o)
    {
        if (!o.Path.StartsWith('/'))
        {
            throw new InvalidOperationException($"Path must start with '/', not '{o.Path}'.");
        }

        AtLeast(nameof(o.MaxMessageBytes), o.MaxMessageBytes, 1024);
        AtLeast(nameof(o.MaxPayloadBytes), o.MaxPayloadBytes, 256);
        if (o.MaxPayloadBytes > o.MaxMessageBytes)
        {
            throw new InvalidOperationException(
                $"MaxPayloadBytes ({o.MaxPayloadBytes}) must be at most MaxMessageBytes ({o.MaxMessageBytes}).");
        }

        AtLeast(nameof(o.MaxPeersPerRoom), o.MaxPeersPerRoom, 2);
        AtLeast(nameof(o.MaxRooms), o.MaxRooms, 1);
        AtLeast(nameof(o.MaxRoomIdLength), o.MaxRoomIdLength, 1);
        AtLeast(nameof(o.MaxMessagesPerSecond), o.MaxMessagesPerSecond, 0);
    }

    private static void AtLeast(string setting, int value, int minimum)
    {
        if (value < minimum)
        {
            throw new InvalidOperationException($"{setting} ({value}) must be at least {minimum}.");
        }
    }

    private static async Task RunAsync(HttpContext ctx)
    {
        // Without UseWebSockets() there is no upgrade feature at all, and IsWebSocketRequest is false for
        // every request — so a host that forgot it would see the relay refuse perfectly good clients with
        // a bare 400 and nothing to go on. Tell it apart from an actual non-WebSocket request.
        if (ctx.Features.Get<IHttpWebSocketFeature>() is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsync(
                "The Rask signaling relay needs WebSocket support in the pipeline. Call app.UseWebSockets() "
                + "before app.MapRaskSignaling(). (Rask.Server's MapRask() does this for you; a static-file "
                + "host serving a published WASM bundle does not.)", ctx.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var options = ctx.RequestServices.GetRequiredService<SignalingOptions>();
        var hub = ctx.RequestServices.GetRequiredService<SignalingHub>();

        using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        Peer? peer = null;
        try
        {
            peer = await PumpAsync(ctx, socket, hub, options).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The client went away, or the host is shutting down. Neither is an error.
        }
        catch (WebSocketException)
        {
            // An abrupt disconnect. Also not an error — the finally below still cleans up.
        }
        finally
        {
            if (peer is not null)
            {
                hub.Leave(peer);
                await AnnounceAsync(hub, peer, "peer-left", ctx.RequestAborted).ConfigureAwait(false);
            }
        }
    }

    // Returns the joined peer (or null if the socket closed before joining) so the caller can clean up
    // exactly once, whichever way the loop ended.
    private static async Task<Peer?> PumpAsync(
        HttpContext ctx, WebSocket socket, SignalingHub hub, SignalingOptions options)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(options.MaxMessageBytes);
        Peer? peer = null;
        var windowStart = Environment.TickCount64;
        var inWindow = 0;

        try
        {
            while (socket.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
            {
                var (bytes, closed) = await ReceiveAsync(socket, buffer, ctx.RequestAborted).ConfigureAwait(false);
                if (closed)
                {
                    return peer;
                }

                if (bytes < 0)
                {
                    // Oversized: the message never fits, so there is nothing to resynchronise to.
                    await CloseAsync(socket, "message too large", ctx.RequestAborted).ConfigureAwait(false);
                    return peer;
                }

                if (options.MaxMessagesPerSecond > 0)
                {
                    var now = Environment.TickCount64;
                    if (now - windowStart >= 1000)
                    {
                        windowStart = now;
                        inWindow = 0;
                    }

                    if (++inWindow > options.MaxMessagesPerSecond)
                    {
                        await CloseAsync(socket, "rate limit", ctx.RequestAborted).ConfigureAwait(false);
                        return peer;
                    }
                }

                peer = await HandleAsync(ctx, socket, hub, options, buffer.AsMemory(0, bytes), peer).ConfigureAwait(false);
            }

            return peer;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // bytes < 0 signals "larger than the cap"; closed signals a clean or client-initiated close.
    private static async Task<(int Bytes, bool Closed)> ReceiveAsync(
        WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Complete the handshake, so a client that called CloseAsync (and is waiting for our close
                // frame) isn't left hanging until its own timeout. CloseOutputAsync rather than CloseAsync:
                // we are already the side that received the close, so there is nothing left to wait for.
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException
                                               or ObjectDisposedException)
                {
                    // Already torn down; the reply was a courtesy.
                }

                return (0, true);
            }

            offset += result.Count;
            if (result.EndOfMessage)
            {
                return (offset, false);
            }

            if (offset >= buffer.Length)
            {
                return (-1, false);
            }
        }
    }

    private static async Task<Peer?> HandleAsync(
        HttpContext ctx, WebSocket socket, SignalingHub hub, SignalingOptions options,
        ReadOnlyMemory<byte> message, Peer? peer)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException)
        {
            await SendErrorAsync(socket, "malformed message", ctx.RequestAborted).ConfigureAwait(false);
            return peer;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
            {
                await SendErrorAsync(socket, "malformed message", ctx.RequestAborted).ConfigureAwait(false);
                return peer;
            }

            return typeEl.GetString() switch
            {
                "join" => await Join(ctx, socket, hub, options, root, peer).ConfigureAwait(false),
                "signal" => await SignalAsync(ctx, socket, hub, options, root, peer).ConfigureAwait(false),
                _ => await Unknown(socket, ctx.RequestAborted, peer).ConfigureAwait(false)
            };
        }

        static async Task<Peer?> Unknown(WebSocket socket, CancellationToken ct, Peer? peer)
        {
            await SendErrorAsync(socket, "unknown message type", ct).ConfigureAwait(false);
            return peer;
        }
    }

    private static async Task<Peer?> Join(
        HttpContext ctx, WebSocket socket, SignalingHub hub, SignalingOptions options,
        JsonElement root, Peer? peer)
    {
        if (peer is not null)
        {
            await SendErrorAsync(socket, "already joined", ctx.RequestAborted).ConfigureAwait(false);
            return peer;
        }

        if (!root.TryGetProperty("room", out var roomEl) || roomEl.ValueKind != JsonValueKind.String)
        {
            await SendErrorAsync(socket, "join needs a room", ctx.RequestAborted).ConfigureAwait(false);
            return null;
        }

        var room = roomEl.GetString()!;
        if (room.Length == 0 || room.Length > options.MaxRoomIdLength)
        {
            await SendErrorAsync(socket, "invalid room", ctx.RequestAborted).ConfigureAwait(false);
            return null;
        }

        var user = ctx.User ?? new System.Security.Claims.ClaimsPrincipal();
        if (!await options.AuthorizeRoom(new SignalingJoinContext(room, user, ctx.RequestServices)).ConfigureAwait(false))
        {
            // Same wording as a full room on purpose: whether a room exists, and who is in it, is not
            // something an unauthorized caller should be able to probe.
            await SendErrorAsync(socket, "cannot join", ctx.RequestAborted).ConfigureAwait(false);
            return null;
        }

        var joined = hub.Join(room, socket, out var existing);
        if (joined is null)
        {
            await SendErrorAsync(socket, "cannot join", ctx.RequestAborted).ConfigureAwait(false);
            return null;
        }

        await SendAsync(socket, joined.SendGate, Json(w =>
        {
            w.WriteString("type", "joined");
            w.WriteString("peerId", joined.Id);
            w.WriteStartArray("peers");
            foreach (var id in existing)
            {
                w.WriteStringValue(id);
            }

            w.WriteEndArray();
        }), ctx.RequestAborted).ConfigureAwait(false);

        await AnnounceAsync(hub, joined, "peer-joined", ctx.RequestAborted).ConfigureAwait(false);
        return joined;
    }

    private static async Task<Peer?> SignalAsync(
        HttpContext ctx, WebSocket socket, SignalingHub hub, SignalingOptions options,
        JsonElement root, Peer? peer)
    {
        if (peer is null)
        {
            await SendErrorAsync(socket, "join first", ctx.RequestAborted).ConfigureAwait(false);
            return null;
        }

        if (!root.TryGetProperty("to", out var toEl) || toEl.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.String)
        {
            await SendErrorAsync(socket, "signal needs `to` and `payload`", ctx.RequestAborted).ConfigureAwait(false);
            return peer;
        }

        var payload = payloadEl.GetString()!;
        if (Encoding.UTF8.GetByteCount(payload) > options.MaxPayloadBytes)
        {
            await SendErrorAsync(socket, "payload too large", ctx.RequestAborted).ConfigureAwait(false);
            return peer;
        }

        // Membership is checked here, not trusted from the message — this is what stops a peer addressing
        // someone in another room, or addressing itself to have the relay echo for it.
        var target = hub.Target(peer, toEl.GetString()!);
        if (target is null)
        {
            await SendErrorAsync(socket, "no such peer", ctx.RequestAborted).ConfigureAwait(false);
            return peer;
        }

        await SendAsync(target.Socket, target.SendGate, Json(w =>
        {
            w.WriteString("type", "signal");
            w.WriteString("from", peer.Id);
            w.WriteString("payload", payload);
        }), ctx.RequestAborted).ConfigureAwait(false);

        return peer;
    }

    private static async Task AnnounceAsync(SignalingHub hub, Peer peer, string type, CancellationToken ct)
    {
        var frame = Json(w =>
        {
            w.WriteString("type", type);
            w.WriteString("peerId", peer.Id);
        });

        foreach (var other in hub.Others(peer))
        {
            // One unreachable peer must not stop the others being told.
            try
            {
                await SendAsync(other.Socket, other.SendGate, frame, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException
                                           or ObjectDisposedException)
            {
                // A peer that has gone away is the normal case here, not a fault: it is why we are
                // announcing in the first place. Its own socket loop reports the disconnect.
            }
        }
    }

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(256);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static Task SendErrorAsync(WebSocket socket, string message, CancellationToken ct) =>
        SendAsync(socket, null, Json(w =>
        {
            w.WriteString("type", "error");
            w.WriteString("message", message);
        }), ct);

    private static async Task SendAsync(
        WebSocket socket, SemaphoreSlim? gate, byte[] frame, CancellationToken ct)
    {
        if (gate is not null)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
        }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(frame, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            gate?.Release();
        }
    }

    private static async Task CloseAsync(WebSocket socket, string reason, CancellationToken ct)
    {
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException
                                       or ObjectDisposedException)
        {
            // Already gone; the close frame was a courtesy.
        }
    }
}
